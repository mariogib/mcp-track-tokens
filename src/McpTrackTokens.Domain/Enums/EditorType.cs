namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Supported editor / IDE environments that can emit tracking events.
/// </summary>
public enum EditorType
{
    /// <summary>Cursor editor.</summary>
    Cursor = 0,

    /// <summary>Visual Studio Code.</summary>
    VisualStudioCode = 1,

    /// <summary>Any other editor.</summary>
    Other = 2
}
