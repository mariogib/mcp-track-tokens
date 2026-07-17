using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// Metrics for a project used by subscription allocation.
/// </summary>
/// <param name="ProjectKey">Caller-defined project identifier.</param>
/// <param name="ActiveTimeSeconds">Active project time in seconds.</param>
/// <param name="PromptCount">Number of prompts.</param>
/// <param name="AgentDurationMilliseconds">Total agent duration in milliseconds.</param>
/// <param name="ManualPercentage">Optional manual percentage for <see cref="AllocationRuleType.ManualPercentage"/>.</param>
public sealed record ProjectAllocationMetrics(
    string ProjectKey,
    long ActiveTimeSeconds = 0,
    long PromptCount = 0,
    long AgentDurationMilliseconds = 0,
    decimal? ManualPercentage = null);

/// <summary>
/// Allocates fixed subscription cost across projects using configured rule types.
/// </summary>
/// <remarks>
/// Subscription allocation is separate from usage-based cost and must never be mixed with it.
/// </remarks>
public sealed class SubscriptionAllocationCalculator
{
    private readonly CostAllocationCalculator _costAllocationCalculator = new();

    /// <summary>
    /// Allocates a subscription amount across projects using the specified rule.
    /// </summary>
    /// <param name="totalAmount">Subscription amount to allocate.</param>
    /// <param name="ruleType">Allocation rule.</param>
    /// <param name="projects">Project metrics. Empty yields an empty result for <see cref="AllocationRuleType.NotAllocated"/>.</param>
    /// <param name="decimals">Amount decimal places.</param>
    public IReadOnlyList<AllocationShare> Allocate(
        decimal totalAmount,
        AllocationRuleType ruleType,
        IReadOnlyList<ProjectAllocationMetrics> projects,
        int decimals = 2)
    {
        Guard.AgainstNull(projects);
        Guard.AgainstNegative(totalAmount);

        return ruleType switch
        {
            AllocationRuleType.NotAllocated => Array.Empty<AllocationShare>(),
            AllocationRuleType.EqualAcrossActiveProjects => AllocateEqual(totalAmount, projects, decimals),
            AllocationRuleType.ByActiveProjectTime => AllocateByWeight(
                totalAmount,
                projects,
                p => p.ActiveTimeSeconds,
                decimals),
            AllocationRuleType.ByPromptCount => AllocateByWeight(
                totalAmount,
                projects,
                p => p.PromptCount,
                decimals),
            AllocationRuleType.ByAgentDuration => AllocateByWeight(
                totalAmount,
                projects,
                p => p.AgentDurationMilliseconds,
                decimals),
            AllocationRuleType.ManualPercentage => AllocateManual(totalAmount, projects, decimals),
            AllocationRuleType.TimeWindowMatch or AllocationRuleType.ProportionalTimeAllocation =>
                AllocateByWeight(totalAmount, projects, p => p.ActiveTimeSeconds, decimals),
            _ => throw new AttributionException($"Unsupported subscription allocation rule: {ruleType}.")
        };
    }

    private IReadOnlyList<AllocationShare> AllocateEqual(
        decimal totalAmount,
        IReadOnlyList<ProjectAllocationMetrics> projects,
        int decimals)
    {
        if (projects.Count == 0)
        {
            return Array.Empty<AllocationShare>();
        }

        var weights = projects
            .Select(p => new AllocationWeight(Guard.AgainstNullOrWhiteSpace(p.ProjectKey), 1m))
            .ToArray();

        return _costAllocationCalculator.AllocateProportionally(totalAmount, weights, decimals);
    }

    private IReadOnlyList<AllocationShare> AllocateByWeight(
        decimal totalAmount,
        IReadOnlyList<ProjectAllocationMetrics> projects,
        Func<ProjectAllocationMetrics, long> weightSelector,
        int decimals)
    {
        if (projects.Count == 0)
        {
            return Array.Empty<AllocationShare>();
        }

        var weights = projects
            .Select(p =>
            {
                var weight = weightSelector(p);
                Guard.AgainstNegative(weight);
                return new AllocationWeight(Guard.AgainstNullOrWhiteSpace(p.ProjectKey), weight);
            })
            .ToArray();

        if (weights.All(w => w.Weight == 0))
        {
            return AllocateEqual(totalAmount, projects, decimals);
        }

        return _costAllocationCalculator.AllocateProportionally(totalAmount, weights, decimals);
    }

    private IReadOnlyList<AllocationShare> AllocateManual(
        decimal totalAmount,
        IReadOnlyList<ProjectAllocationMetrics> projects,
        int decimals)
    {
        if (projects.Count == 0)
        {
            return Array.Empty<AllocationShare>();
        }

        var targets = new List<(string Key, decimal Percentage)>(projects.Count);
        foreach (var project in projects)
        {
            if (project.ManualPercentage is null)
            {
                throw new AttributionException(
                    $"Project '{project.ProjectKey}' is missing ManualPercentage for manual allocation.");
            }

            targets.Add((
                Guard.AgainstNullOrWhiteSpace(project.ProjectKey),
                project.ManualPercentage.Value));
        }

        var sum = targets.Sum(t => t.Percentage);
        if (sum <= 0)
        {
            throw new AttributionException("Manual percentages must sum to a positive value.");
        }

        return _costAllocationCalculator.AllocateByPercentages(totalAmount, targets, decimals);
    }
}
