using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Calculates, merges, and persists activity windows using the domain calculator.
/// </summary>
public sealed class ActivityWindowService : IActivityWindowService
{
    private readonly IActivityEventRepository _events;
    private readonly IActivityWindowRepository _windows;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ActivityWindowCalculator _calculator;
    private readonly TrackingOptions _options;

    public ActivityWindowService(
        IActivityEventRepository events,
        IActivityWindowRepository windows,
        IUnitOfWork unitOfWork,
        ActivityWindowCalculator calculator,
        IOptions<TrackingOptions> options)
    {
        _events = events;
        _windows = windows;
        _unitOfWork = unitOfWork;
        _calculator = calculator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<RecalculateWindowsResultDto> RecalculateAsync(
        Guid? projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? inactivityThresholdMinutes = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var threshold = inactivityThresholdMinutes ?? _options.InactivityThresholdMinutes;
        var events = await _events
            .ListAsync(fromUtc, toUtc, projectId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var groups = events
            .GroupBy(e => (e.ProjectId, e.EditorSessionId))
            .ToList();

        var created = new List<ActivityWindow>();
        foreach (var group in groups)
        {
            var timestamps = group
                .Select(e => new ActivityTimestamp(e.TimestampUtc, e.EventType))
                .ToList();
            var calculated = _calculator.Calculate(timestamps, threshold);
            foreach (var window in calculated)
            {
                created.Add(ActivityWindow.Create(
                    window.StartedAtUtc,
                    window.EndedAtUtc,
                    threshold,
                    group.Key.ProjectId,
                    group.Key.EditorSessionId,
                    window.CalculationVersion));
            }
        }

        var merged = MergeOverlappingSameProjectWindows(created);
        var totalSeconds = merged.Sum(w => w.DurationSeconds);

        if (!dryRun)
        {
            await _windows.DeleteForScopeAsync(projectId, null, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
            if (merged.Count > 0)
            {
                await _windows.AddRangeAsync(merged, cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RecalculateWindowsResultDto
        {
            DryRun = dryRun,
            ProjectId = projectId,
            WindowCount = merged.Count,
            TotalActiveSeconds = totalSeconds,
            CalculationVersion = ActivityWindowCalculator.CalculationVersion
        };
    }

    /// <inheritdoc />
    public async Task UpdateForEventAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);

        if (!ActivityWindowCalculator.IsWindowRelevant(activityEvent.EventType))
        {
            return;
        }

        // Recalculate a local neighbourhood around the event to keep windows consistent.
        var threshold = TimeSpan.FromMinutes(_options.InactivityThresholdMinutes);
        var fromUtc = activityEvent.TimestampUtc - threshold - threshold;
        var toUtc = activityEvent.TimestampUtc + threshold + threshold;

        await RecalculateAsync(
                activityEvent.ProjectId,
                fromUtc,
                toUtc,
                _options.InactivityThresholdMinutes,
                dryRun: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivityWindow> MergeOverlappingSameProjectWindows(IEnumerable<ActivityWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var result = new List<ActivityWindow>();
        foreach (var group in windows
                     .Where(w => w.ProjectId is not null)
                     .GroupBy(w => w.ProjectId!.Value))
        {
            var ordered = group.OrderBy(w => w.StartedAtUtc).ThenBy(w => w.EndedAtUtc).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }

            var currentStart = ordered[0].StartedAtUtc;
            var currentEnd = ordered[0].EndedAtUtc;
            var threshold = ordered[0].InactivityThresholdMinutes;
            var sessionId = ordered[0].EditorSessionId;
            var projectId = ordered[0].ProjectId;

            for (var i = 1; i < ordered.Count; i++)
            {
                var next = ordered[i];
                if (next.StartedAtUtc <= currentEnd)
                {
                    if (next.EndedAtUtc > currentEnd)
                    {
                        currentEnd = next.EndedAtUtc;
                    }

                    // Mixed sessions collapse to null session for reporting merges.
                    if (sessionId != next.EditorSessionId)
                    {
                        sessionId = null;
                    }

                    continue;
                }

                result.Add(ActivityWindow.Create(currentStart, currentEnd, threshold, projectId, sessionId));
                currentStart = next.StartedAtUtc;
                currentEnd = next.EndedAtUtc;
                threshold = next.InactivityThresholdMinutes;
                sessionId = next.EditorSessionId;
                projectId = next.ProjectId;
            }

            result.Add(ActivityWindow.Create(currentStart, currentEnd, threshold, projectId, sessionId));
        }

        // Preserve windows without a project (unallocated) without merging across projects.
        result.AddRange(windows.Where(w => w.ProjectId is null));
        return result
            .OrderBy(w => w.StartedAtUtc)
            .ThenBy(w => w.EndedAtUtc)
            .ToList();
    }
}
