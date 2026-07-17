namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Status of an import batch.
/// </summary>
public enum ImportStatus
{
    /// <summary>Import is queued.</summary>
    Pending = 0,

    /// <summary>Import is in preview mode.</summary>
    Preview = 1,

    /// <summary>Import is currently running.</summary>
    InProgress = 2,

    /// <summary>Import completed successfully.</summary>
    Completed = 3,

    /// <summary>Import failed.</summary>
    Failed = 4,

    /// <summary>Import completed with some failures.</summary>
    Partial = 5
}
