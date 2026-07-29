using FluentAssertions;
using McpTrackTokens.Application;

namespace McpTrackTokens.Application.Tests;

public sealed class IntervalOverlapTests
{
    [Fact]
    public void UnionSeconds_merges_overlapping_sessions_without_double_counting()
    {
        var from = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-18T00:00:00Z");
        var intervals = new (DateTimeOffset, DateTimeOffset?)[]
        {
            (DateTimeOffset.Parse("2026-07-17T15:00:00Z"), DateTimeOffset.Parse("2026-07-17T20:00:00Z")),
            (DateTimeOffset.Parse("2026-07-17T16:00:00Z"), DateTimeOffset.Parse("2026-07-17T21:00:00Z")),
            (DateTimeOffset.Parse("2026-07-17T18:00:00Z"), DateTimeOffset.Parse("2026-07-17T22:00:00Z")),
        };

        var summed = intervals.Sum(i => IntervalOverlap.Seconds(i.Item1, i.Item2, from, to));
        var union = IntervalOverlap.UnionSeconds(intervals, from, to);

        summed.Should().Be(5 * 3600 + 5 * 3600 + 4 * 3600);
        union.Should().Be(7 * 3600);
    }

    [Fact]
    public void CountOverlapping_counts_sessions_that_touch_the_day()
    {
        var from = DateTimeOffset.Parse("2026-07-18T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-18T23:59:59Z");
        var intervals = new (DateTimeOffset, DateTimeOffset?)[]
        {
            (DateTimeOffset.Parse("2026-07-17T15:00:00Z"), DateTimeOffset.Parse("2026-07-18T12:00:00Z")),
            (DateTimeOffset.Parse("2026-07-18T10:00:00Z"), DateTimeOffset.Parse("2026-07-18T11:00:00Z")),
            (DateTimeOffset.Parse("2026-07-16T10:00:00Z"), DateTimeOffset.Parse("2026-07-17T11:00:00Z")),
        };

        IntervalOverlap.CountOverlapping(intervals, from, to).Should().Be(2);
    }

    [Fact]
    public void CountOverlapping_includes_sub_second_sessions()
    {
        var from = DateTimeOffset.Parse("2026-07-19T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-19T23:59:59.9999999Z");
        var intervals = new (DateTimeOffset, DateTimeOffset?)[]
        {
            (DateTimeOffset.Parse("2026-07-19T10:44:06.0616126Z"),
                DateTimeOffset.Parse("2026-07-19T10:44:06.4106034Z")),
            (DateTimeOffset.Parse("2026-07-19T10:44:06.4106034Z"),
                DateTimeOffset.Parse("2026-07-19T10:44:06.5029638Z")),
        };

        IntervalOverlap.CountOverlapping(intervals, from, to).Should().Be(2);
        IntervalOverlap.Seconds(intervals[0].Item1, intervals[0].Item2, from, to).Should().Be(0);
    }
}
