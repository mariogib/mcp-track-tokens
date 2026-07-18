namespace McpTrackTokens.Domain.Services;

/// <summary>
/// Timestamp helpers for attribution matching (second-precision compare).
/// </summary>
public static class TimestampPrecision
{
    /// <summary>
    /// Rounds <paramref name="value"/> to the nearest whole UTC second.
    /// </summary>
    public static DateTimeOffset RoundToSecond(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var millis = utc.ToUnixTimeMilliseconds();
        var rounded = (millis + (millis >= 0 ? 500 : -500)) / 1000 * 1000;
        return DateTimeOffset.FromUnixTimeMilliseconds(rounded);
    }
}
