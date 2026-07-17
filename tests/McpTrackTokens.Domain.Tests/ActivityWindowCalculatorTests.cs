using FluentAssertions;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Tests;

public sealed class ActivityWindowCalculatorTests
{
    [Fact]
    public void Calculate_matches_documented_fifteen_minute_example()
    {
        // 09:00 prompt, 09:08 prompt, 09:14 agent completed, 09:31 prompt
        // with 15 min threshold => Window1 09:00–09:29, Window2 09:31–09:46
        var day = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        var activities = new[]
        {
            new ActivityTimestamp(day.AddHours(9), ActivityEventType.PromptSubmitted),
            new ActivityTimestamp(day.AddHours(9).AddMinutes(8), ActivityEventType.PromptSubmitted),
            new ActivityTimestamp(day.AddHours(9).AddMinutes(14), ActivityEventType.AgentCompleted),
            new ActivityTimestamp(day.AddHours(9).AddMinutes(31), ActivityEventType.PromptSubmitted)
        };

        var calculator = new ActivityWindowCalculator();
        var windows = calculator.Calculate(activities, thresholdMinutes: 15);

        windows.Should().HaveCount(2);

        windows[0].StartedAtUtc.Should().Be(day.AddHours(9));
        windows[0].LastActivityAtUtc.Should().Be(day.AddHours(9).AddMinutes(14));
        windows[0].EndedAtUtc.Should().Be(day.AddHours(9).AddMinutes(29));
        windows[0].InactivityThresholdMinutes.Should().Be(15);
        windows[0].CalculationVersion.Should().Be(ActivityWindowCalculator.CalculationVersion);

        windows[1].StartedAtUtc.Should().Be(day.AddHours(9).AddMinutes(31));
        windows[1].LastActivityAtUtc.Should().Be(day.AddHours(9).AddMinutes(31));
        windows[1].EndedAtUtc.Should().Be(day.AddHours(9).AddMinutes(46));
    }

    [Fact]
    public void Calculate_returns_empty_when_no_relevant_events()
    {
        var calculator = new ActivityWindowCalculator();
        var windows = calculator.Calculate(
        [
            new ActivityTimestamp(DateTimeOffset.UtcNow, ActivityEventType.SessionStarted),
            new ActivityTimestamp(DateTimeOffset.UtcNow, ActivityEventType.WorkspaceChanged)
        ]);

        windows.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_extends_window_within_threshold()
    {
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var calculator = new ActivityWindowCalculator();
        var windows = calculator.Calculate(
        [
            new ActivityTimestamp(start, ActivityEventType.PromptSubmitted),
            new ActivityTimestamp(start.AddMinutes(10), ActivityEventType.Heartbeat)
        ], thresholdMinutes: 15);

        windows.Should().HaveCount(1);
        windows[0].StartedAtUtc.Should().Be(start);
        windows[0].EndedAtUtc.Should().Be(start.AddMinutes(25));
    }
}
