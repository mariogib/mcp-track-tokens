using FluentAssertions;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Tests;

public sealed class PercentageTests
{
    [Fact]
    public void EnsureSumTo100_rounds_so_percentages_sum_exactly_to_100()
    {
        // 1/3 each would be 33.333... -> 33.33, 33.33, 33.34
        var results = Percentage.EnsureSumTo100([100m / 3m, 100m / 3m, 100m / 3m]);

        results.Select(p => p.Value).Should().Equal(33.33m, 33.33m, 33.34m);
        results.Sum(p => p.Value).Should().Be(100m);
    }

    [Fact]
    public void EnsureSumTo100_scales_relative_weights_to_100()
    {
        var results = Percentage.EnsureSumTo100([1m, 1m, 2m]);

        results.Select(p => p.Value).Should().Equal(25m, 25m, 50m);
        results.Sum(p => p.Value).Should().Be(100m);
    }

    [Fact]
    public void EqualParts_sums_to_100()
    {
        var parts = Percentage.EqualParts(3);
        parts.Sum(p => p.Value).Should().Be(100m);
    }

    [Fact]
    public void FromRatio_and_ToRatio_round_trip()
    {
        var percentage = Percentage.FromRatio(0.25m);
        percentage.Value.Should().Be(25m);
        percentage.ToRatio().Should().Be(0.25m);
    }
}
