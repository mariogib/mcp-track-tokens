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

    /// <summary>
    /// Returns whole seconds of coverage for <paramref name="intervals"/> inside
    /// <c>[fromUtc, toUtc]</c>, merging overlaps so concurrent sessions are not double-counted.
    /// </summary>
    public static long UnionSeconds(
        IEnumerable<(DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc)> intervals,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        var clipped = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (startedAtUtc, endedAtUtc) in intervals)
        {
            var start = startedAtUtc.ToUniversalTime();
            if (start < from)
            {
                start = from;
            }

            var end = (endedAtUtc ?? now).ToUniversalTime();
            if (end > to)
            {
                end = to;
            }

            if (end > start)
            {
                clipped.Add((start, end));
            }
        }

        if (clipped.Count == 0)
        {
            return 0;
        }

        clipped.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        long total = 0;
        var mergeStart = clipped[0].Start;
        var mergeEnd = clipped[0].End;
        for (var i = 1; i < clipped.Count; i++)
        {
            var (start, end) = clipped[i];
            if (start <= mergeEnd)
            {
                if (end > mergeEnd)
                {
                    mergeEnd = end;
                }

                continue;
            }

            total += (long)Math.Floor((mergeEnd - mergeStart).TotalSeconds);
            mergeStart = start;
            mergeEnd = end;
        }

        total += (long)Math.Floor((mergeEnd - mergeStart).TotalSeconds);
        return total;
    }

    /// <summary>
    /// Counts intervals that overlap <c>[fromUtc, toUtc]</c> (open-ended intervals use <paramref name="nowUtc"/>).
    /// Sub-second overlaps still count (same as timesheet/session list overlap).
    /// </summary>
    public static int CountOverlapping(
        IEnumerable<(DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc)> intervals,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        return intervals.Count(interval =>
        {
            var start = interval.StartedAtUtc.ToUniversalTime();
            var end = (interval.EndedAtUtc ?? now).ToUniversalTime();
            return start <= to && end >= from && end >= start;
        });
    }
}
