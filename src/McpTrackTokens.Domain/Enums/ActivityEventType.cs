namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Types of activity events captured from editors and agents.
/// </summary>
public enum ActivityEventType
{
    /// <summary>A user prompt was submitted.</summary>
    PromptSubmitted = 0,

    /// <summary>An agent run started.</summary>
    AgentStarted = 1,

    /// <summary>An agent run completed successfully.</summary>
    AgentCompleted = 2,

    /// <summary>An agent run was cancelled.</summary>
    AgentCancelled = 3,

    /// <summary>An agent run failed.</summary>
    AgentFailed = 4,

    /// <summary>A tool invocation started.</summary>
    ToolStarted = 5,

    /// <summary>A tool invocation completed.</summary>
    ToolCompleted = 6,

    /// <summary>A tracking session started.</summary>
    SessionStarted = 7,

    /// <summary>A tracking session ended.</summary>
    SessionEnded = 8,

    /// <summary>The active workspace changed.</summary>
    WorkspaceChanged = 9,

    /// <summary>A keepalive heartbeat from the editor.</summary>
    Heartbeat = 10
}
