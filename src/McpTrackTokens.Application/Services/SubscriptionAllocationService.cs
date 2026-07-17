using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Allocates fixed subscription cost separately from usage-based cost.
/// </summary>
public sealed class SubscriptionAllocationService : ISubscriptionAllocationService
{
    private readonly IProjectRepository _projects;
    private readonly IActivityEventRepository _events;
    private readonly IActivityWindowRepository _windows;
    private readonly SubscriptionAllocationCalculator _calculator;
    private readonly TrackingOptions _options;

    public SubscriptionAllocationService(
        IProjectRepository projects,
        IActivityEventRepository events,
        IActivityWindowRepository windows,
        SubscriptionAllocationCalculator calculator,
        IOptions<TrackingOptions> options)
    {
        _projects = projects;
        _events = events;
        _windows = windows;
        _calculator = calculator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectAllocationShareDto>> AllocateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        AllocationRuleType? method = null,
        decimal? amount = null,
        string? currency = null,
        IReadOnlyDictionary<Guid, decimal>? manualPercentages = null,
        CancellationToken cancellationToken = default)
    {
        var rule = method ?? _options.CursorAllocationMethod;
        var total = amount ?? _options.CursorSubscriptionAmount;
        _ = currency ?? _options.CursorSubscriptionCurrency;

        if (rule == AllocationRuleType.NotAllocated || total <= 0m)
        {
            return [];
        }

        var projects = await _projects.ListAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
        var metrics = new List<ProjectAllocationMetrics>(projects.Count);

        foreach (var project in projects)
        {
            var events = await _events
                .ListAsync(fromUtc, toUtc, project.Id, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var promptCount = events.Count(e => e.EventType == Domain.Enums.ActivityEventType.PromptSubmitted);
            var agentDuration = events
                .Where(e => e.EventType is Domain.Enums.ActivityEventType.AgentCompleted
                    or Domain.Enums.ActivityEventType.AgentFailed
                    or Domain.Enums.ActivityEventType.AgentCancelled)
                .Sum(e => e.DurationMilliseconds ?? 0);

            var activeSeconds = await _windows
                .SumDurationSecondsAsync(fromUtc, toUtc, project.Id, cancellationToken)
                .ConfigureAwait(false);

            decimal? manual = null;
            if (manualPercentages is not null && manualPercentages.TryGetValue(project.Id, out var pct))
            {
                manual = pct;
            }

            // Only include projects with activity for equal/time/prompt/agent methods,
            // except ManualPercentage which uses the provided map.
            var hasActivity = promptCount > 0 || agentDuration > 0 || activeSeconds > 0;
            if (!hasActivity && rule is not AllocationRuleType.ManualPercentage and not AllocationRuleType.EqualAcrossActiveProjects)
            {
                continue;
            }

            if (rule == AllocationRuleType.EqualAcrossActiveProjects && !hasActivity)
            {
                continue;
            }

            if (rule == AllocationRuleType.ManualPercentage && manual is null)
            {
                continue;
            }

            metrics.Add(new ProjectAllocationMetrics(
                project.Id.ToString("D"),
                activeSeconds,
                promptCount,
                agentDuration,
                manual));
        }

        if (metrics.Count == 0)
        {
            return [];
        }

        var shares = _calculator.Allocate(total, rule, metrics);
        return shares
            .Select(s => new ProjectAllocationShareDto
            {
                ProjectId = Guid.Parse(s.Key),
                Percentage = s.Percentage.Value
            })
            .ToList();
    }
}
