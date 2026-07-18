namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Method used to attribute usage or cost to a project.
/// </summary>
public enum AttributionMethod
{
    /// <summary>Attributed from an explicitly reported repository.</summary>
    RepositoryReported = 0,

    /// <summary>Attributed from an explicit project identifier.</summary>
    ExplicitProject = 1,

    /// <summary>Matched via an external session identifier.</summary>
    ExternalSessionMatch = 2,

    /// <summary>Only one active session existed at the usage timestamp.</summary>
    SingleActiveSession = 3,

    /// <summary>Matched by overlapping activity time windows.</summary>
    TimeWindowMatch = 4,

    /// <summary>Allocated proportionally across overlapping project time.</summary>
    ProportionalTimeAllocation = 5,

    /// <summary>Manually attributed by a user.</summary>
    Manual = 6,

    /// <summary>Could not be attributed to any project.</summary>
    Unallocated = 7,

    /// <summary>
    /// Matched to the closest prompt at or before the usage timestamp (second precision).
    /// </summary>
    ClosestPromptMatch = 8
}
