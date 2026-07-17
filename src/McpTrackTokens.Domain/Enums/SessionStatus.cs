namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Lifecycle status of an editor tracking session.
/// </summary>
public enum SessionStatus
{
    /// <summary>Session is currently active.</summary>
    Active = 0,

    /// <summary>Session is temporarily paused.</summary>
    Paused = 1,

    /// <summary>Session ended normally.</summary>
    Ended = 2,

    /// <summary>Session was abandoned without a clean end.</summary>
    Abandoned = 3
}
