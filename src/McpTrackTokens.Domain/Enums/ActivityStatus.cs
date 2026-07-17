namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Outcome status for an activity event.
/// </summary>
public enum ActivityStatus
{
    /// <summary>The activity has started.</summary>
    Started = 0,

    /// <summary>The activity completed successfully.</summary>
    Completed = 1,

    /// <summary>The activity was cancelled.</summary>
    Cancelled = 2,

    /// <summary>The activity failed.</summary>
    Failed = 3,

    /// <summary>The status is unknown.</summary>
    Unknown = 4
}
