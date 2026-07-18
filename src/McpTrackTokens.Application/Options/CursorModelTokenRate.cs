namespace McpTrackTokens.Application.Options;

/// <summary>
/// Per-model Cursor token rates in currency units per 1,000,000 tokens.
/// </summary>
public sealed class CursorModelTokenRate
{
    /// <summary>
    /// Model name as it appears in Cursor usage exports (e.g. <c>Auto</c>, <c>claude-4.5-sonnet</c>).
    /// Use <c>*</c> for the default fallback rate.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Input token rate per million tokens.
    /// </summary>
    public decimal InputPerMillion { get; set; }

    /// <summary>
    /// Output token rate per million tokens.
    /// </summary>
    public decimal OutputPerMillion { get; set; }

    /// <summary>
    /// Cache-read token rate per million tokens.
    /// </summary>
    public decimal CacheReadPerMillion { get; set; }

    /// <summary>
    /// Cache-write token rate per million tokens.
    /// </summary>
    public decimal CacheWritePerMillion { get; set; }

    /// <summary>
    /// Optional reasoning / thinking token rate per million tokens.
    /// </summary>
    public decimal? ReasoningPerMillion { get; set; }
}
