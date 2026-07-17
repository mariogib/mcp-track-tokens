using FluentAssertions;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Tests;

public sealed class SubscriptionAllocationCalculatorTests
{
    private readonly SubscriptionAllocationCalculator _calculator = new();

    [Fact]
    public void Allocate_NotAllocated_returns_empty()
    {
        var result = _calculator.Allocate(
            100m,
            AllocationRuleType.NotAllocated,
            [new ProjectAllocationMetrics("a", ActiveTimeSeconds: 60)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Allocate_Equal_across_active_projects()
    {
        var result = _calculator.Allocate(
            90m,
            AllocationRuleType.EqualAcrossActiveProjects,
            [
                new ProjectAllocationMetrics("a"),
                new ProjectAllocationMetrics("b"),
                new ProjectAllocationMetrics("c")
            ]);

        result.Select(r => r.Amount).Should().Equal(30m, 30m, 30m);
        result.Sum(r => r.Percentage.Value).Should().Be(100m);
    }

    [Fact]
    public void Allocate_by_active_project_time()
    {
        var result = _calculator.Allocate(
            10m,
            AllocationRuleType.ByActiveProjectTime,
            [
                new ProjectAllocationMetrics("long", ActiveTimeSeconds: 3 * 3600),
                new ProjectAllocationMetrics("short", ActiveTimeSeconds: 3600)
            ]);

        result[0].Amount.Should().Be(7.50m);
        result[1].Amount.Should().Be(2.50m);
    }

    [Fact]
    public void Allocate_by_prompt_count()
    {
        var result = _calculator.Allocate(
            100m,
            AllocationRuleType.ByPromptCount,
            [
                new ProjectAllocationMetrics("hot", PromptCount: 80),
                new ProjectAllocationMetrics("cold", PromptCount: 20)
            ]);

        result[0].Amount.Should().Be(80m);
        result[1].Amount.Should().Be(20m);
    }

    [Fact]
    public void Allocate_by_agent_duration()
    {
        var result = _calculator.Allocate(
            100m,
            AllocationRuleType.ByAgentDuration,
            [
                new ProjectAllocationMetrics("a", AgentDurationMilliseconds: 60000),
                new ProjectAllocationMetrics("b", AgentDurationMilliseconds: 40000)
            ]);

        result[0].Amount.Should().Be(60m);
        result[1].Amount.Should().Be(40m);
    }

    [Fact]
    public void Allocate_manual_percentage()
    {
        var result = _calculator.Allocate(
            200m,
            AllocationRuleType.ManualPercentage,
            [
                new ProjectAllocationMetrics("a", ManualPercentage: 70m),
                new ProjectAllocationMetrics("b", ManualPercentage: 30m)
            ]);

        result[0].Amount.Should().Be(140m);
        result[1].Amount.Should().Be(60m);
    }

    [Fact]
    public void Allocate_manual_percentage_requires_values()
    {
        var act = () => _calculator.Allocate(
            100m,
            AllocationRuleType.ManualPercentage,
            [new ProjectAllocationMetrics("missing")]);

        act.Should().Throw<AttributionException>();
    }

    [Fact]
    public void Allocate_falls_back_to_equal_when_all_weights_zero()
    {
        var result = _calculator.Allocate(
            50m,
            AllocationRuleType.ByPromptCount,
            [
                new ProjectAllocationMetrics("a", PromptCount: 0),
                new ProjectAllocationMetrics("b", PromptCount: 0)
            ]);

        result.Select(r => r.Amount).Should().Equal(25m, 25m);
    }
}
