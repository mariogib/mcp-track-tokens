using FluentAssertions;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Tests;

public sealed class AgentDurationCalculatorTests
{
    [Fact]
    public void SumMilliseconds_prefers_completed_prompt_duration_over_empty_agent_rows()
    {
        var started = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            started,
            projectId: Guid.NewGuid());
        prompt.ApplyCompletion(ActivityStatus.Completed, started.AddMinutes(5));

        var agentRow = PromptActivityEvent.Create(
            ActivityEventType.AgentCompleted,
            EditorType.Cursor,
            started.AddMinutes(5),
            projectId: prompt.ProjectId);

        AgentDurationCalculator.SumMilliseconds([prompt, agentRow]).Should().Be(5 * 60_000);
    }

    [Fact]
    public void SumMilliseconds_falls_back_to_terminal_agent_rows_when_prompts_have_no_duration()
    {
        var at = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var events = new[]
        {
            PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                at,
                projectId: Guid.NewGuid()),
            PromptActivityEvent.Create(
                ActivityEventType.AgentCompleted,
                EditorType.Cursor,
                at.AddMinutes(2),
                projectId: Guid.NewGuid(),
                durationMilliseconds: 90_000),
        };

        AgentDurationCalculator.SumMilliseconds(events).Should().Be(90_000);
    }

    [Fact]
    public void ResolveMilliseconds_uses_response_completed_delta_when_duration_missing()
    {
        var started = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            started,
            responseCompletedAtUtc: started.AddSeconds(45));

        AgentDurationCalculator.ResolveMilliseconds(prompt).Should().Be(45_000);
    }
}
