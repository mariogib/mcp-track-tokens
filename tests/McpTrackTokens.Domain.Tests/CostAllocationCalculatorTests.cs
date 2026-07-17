using FluentAssertions;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Tests;

public sealed class CostAllocationCalculatorTests
{
    [Fact]
    public void AllocateProportionally_splits_three_hours_and_one_hour_of_ten_dollars()
    {
        var calculator = new CostAllocationCalculator();
        var shares = calculator.AllocateProportionally(
            10m,
            [
                new AllocationWeight("project-a", 3m),
                new AllocationWeight("project-b", 1m)
            ]);

        shares.Should().HaveCount(2);
        shares[0].Key.Should().Be("project-a");
        shares[0].Amount.Should().Be(7.50m);
        shares[0].Percentage.Value.Should().Be(75m);

        shares[1].Key.Should().Be("project-b");
        shares[1].Amount.Should().Be(2.50m);
        shares[1].Percentage.Value.Should().Be(25m);

        shares.Sum(s => s.Amount).Should().Be(10m);
        shares.Sum(s => s.Percentage.Value).Should().Be(100m);
    }

    [Fact]
    public void AllocateProportionally_applies_rounding_remainder_to_last_non_zero_weight()
    {
        var calculator = new CostAllocationCalculator();
        // 1/3 each of $10 => 3.33, 3.33, remainder 3.34 on last
        var shares = calculator.AllocateProportionally(
            10m,
            [
                new AllocationWeight("a", 1m),
                new AllocationWeight("b", 1m),
                new AllocationWeight("c", 1m)
            ]);

        shares.Select(s => s.Amount).Should().Equal(3.33m, 3.33m, 3.34m);
        shares.Sum(s => s.Amount).Should().Be(10m);
        shares.Sum(s => s.Percentage.Value).Should().Be(100m);
    }

    [Fact]
    public void AllocateByPercentages_respects_explicit_percentages()
    {
        var calculator = new CostAllocationCalculator();
        var shares = calculator.AllocateByPercentages(
            100m,
            [
                ("alpha", 60m),
                ("beta", 40m)
            ]);

        shares[0].Amount.Should().Be(60m);
        shares[1].Amount.Should().Be(40m);
    }
}
