namespace McpTrackTokens.Application;

/// <summary>
/// Clips open-ended intervals to a report range (same rules as timesheet overlap).
/// </summary>
public static class IntervalOverlap
{
    /// <summary>
    /// Returns whole seconds of overlap between
    /// <c>[startedAtUtc, endedAtUtc ?? now]</c> and <c>[fromUtc, toUtc]</c>.
    /// </summary>
    public static long Seconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        var effectiveStart = startedAtUtc.ToUniversalTime();
        if (effectiveStart < from)
        {
            effectiveStart = from;
        }

        var effectiveEnd = (endedAtUtc ?? now).ToUniversalTime();
        if (effectiveEnd > to)
        {
            effectiveEnd = to;
        }

        if (effectiveEnd <= effectiveStart)
        {
            return 0;
        }

        return (long)Math.Floor((effectiveEnd - effectiveStart).TotalSeconds);
    }
}
