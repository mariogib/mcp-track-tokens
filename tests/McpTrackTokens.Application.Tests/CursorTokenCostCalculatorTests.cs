using FluentAssertions;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Tests;

public sealed class CursorTokenCostCalculatorTests
{
    [Fact]
    public void Estimate_prices_token_buckets_from_rate_card()
    {
        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            model: "claude-4.5-sonnet",
            inputTokens: 1_000_000,
            outputTokens: 500_000,
            cachedInputTokens: 2_000_000,
            reasoningTokens: 100_000,
            totalTokens: 3_600_000);

        var rate = new CursorModelTokenRate
        {
            Model = "claude-4.5-sonnet",
            InputPerMillion = 3m,
            OutputPerMillion = 15m,
            CacheReadPerMillion = 0.3m,
            ReasoningPerMillion = 15m
        };

        var cost = CursorTokenCostCalculator.Estimate(usage, 100m, rate);

        // 1*3 + 0.5*15 + 2*0.3 + 0.1*15 = 3 + 7.5 + 0.6 + 1.5 = 12.6
        cost.Should().Be(12.6m);
    }

    [Fact]
    public void ResolveRate_falls_back_to_star()
    {
        var rates = new List<CursorModelTokenRate>
        {
            new() { Model = "*", InputPerMillion = 1.25m, OutputPerMillion = 6m },
            new() { Model = "Auto", InputPerMillion = 1.25m, OutputPerMillion = 6m }
        };

        var match = CursorTokenCostCalculator.ResolveRate(rates, "gpt-mystery");
        match!.Model.Should().Be("*");
    }
}
