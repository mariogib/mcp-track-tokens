using FluentAssertions;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Add_and_subtract_same_currency()
    {
        var a = new Money(10.50m, "USD");
        var b = new Money(2.25m, "usd");

        (a + b).Amount.Should().Be(12.75m);
        (a - b).Amount.Should().Be(8.25m);
        (a * 2m).Amount.Should().Be(21.00m);
    }

    [Fact]
    public void Add_throws_on_currency_mismatch()
    {
        var a = new Money(1m, "USD");
        var b = new Money(1m, "EUR");
        var act = () => a.Add(b);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void AllocateByWeights_preserves_total_with_remainder()
    {
        var money = new Money(10m, "USD");
        var parts = money.AllocateByWeights([1m, 1m, 1m]);

        parts.Select(p => p.Amount).Should().Equal(3.33m, 3.33m, 3.34m);
        parts.Sum(p => p.Amount).Should().Be(10m);
        parts.Should().OnlyContain(p => p.Currency == "USD");
    }

    [Fact]
    public void AllocateByPercentages_splits_by_percentage()
    {
        var money = new Money(100m, "EUR");
        var parts = money.AllocateByPercentages(
        [
            new Percentage(70m),
            new Percentage(30m)
        ]);

        parts[0].Amount.Should().Be(70m);
        parts[1].Amount.Should().Be(30m);
    }

    [Fact]
    public void Zero_creates_zero_amount()
    {
        Money.Zero("GBP").Should().Be(new Money(0m, "GBP"));
    }
}
