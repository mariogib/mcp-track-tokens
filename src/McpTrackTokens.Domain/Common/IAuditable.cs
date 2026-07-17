namespace McpTrackTokens.Domain.Common;

/// <summary>
/// Marks an entity that tracks its last update time in UTC.
/// </summary>
public interface IAuditable
{
    /// <summary>
    /// UTC timestamp of the most recent update.
    /// </summary>
    DateTimeOffset UpdatedAtUtc { get; }
}
