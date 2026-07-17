namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Rule types for allocating subscription or usage costs across projects.
/// </summary>
public enum AllocationRuleType
{
    /// <summary>Split equally across active projects.</summary>
    EqualAcrossActiveProjects = 0,

    /// <summary>Allocate by active project time.</summary>
    ByActiveProjectTime = 1,

    /// <summary>Allocate by prompt count.</summary>
    ByPromptCount = 2,

    /// <summary>Allocate by agent execution duration.</summary>
    ByAgentDuration = 3,

    /// <summary>Allocate using manually specified percentages.</summary>
    ManualPercentage = 4,

    /// <summary>Do not allocate.</summary>
    NotAllocated = 5,

    /// <summary>Match by activity time windows.</summary>
    TimeWindowMatch = 6,

    /// <summary>Allocate proportionally by overlapping time.</summary>
    ProportionalTimeAllocation = 7
}
