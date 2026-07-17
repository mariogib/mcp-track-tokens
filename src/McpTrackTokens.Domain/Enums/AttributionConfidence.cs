namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Confidence level for a usage attribution decision.
/// </summary>
public enum AttributionConfidence
{
    /// <summary>Attribution is certain.</summary>
    Certain = 0,

    /// <summary>High confidence match.</summary>
    High = 1,

    /// <summary>Medium confidence match.</summary>
    Medium = 2,

    /// <summary>Low confidence match.</summary>
    Low = 3,

    /// <summary>No allocation was made.</summary>
    Unallocated = 4
}
