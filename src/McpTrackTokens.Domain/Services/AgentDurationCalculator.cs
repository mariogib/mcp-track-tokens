using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// Aggregates agent run duration from activity events.
/// Prefers completed <see cref="ActivityEventType.PromptSubmitted"/> rows (where
/// <c>ApplyCompletion</c> stores submit→finish duration); falls back to terminal agent events.
/// </summary>
public static class AgentDurationCalculator
{
    /// <summary>
    /// Sums agent duration in milliseconds for the given events.
    /// </summary>
    public static long SumMilliseconds(IEnumerable<PromptActivityEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = events as IReadOnlyList<PromptActivityEvent> ?? events.ToList();

        long fromPrompts = 0;
        foreach (var prompt in list.Where(e => e.EventType == ActivityEventType.PromptSubmitted))
        {
            fromPrompts += ResolveMilliseconds(prompt);
        }

        if (fromPrompts > 0)
        {
            return fromPrompts;
        }

        return list
            .Where(e => e.EventType is ActivityEventType.AgentCompleted
                or ActivityEventType.AgentFailed
                or ActivityEventType.AgentCancelled)
            .Sum(ResolveMilliseconds);
    }

    /// <summary>
    /// Resolves duration for a single event from <see cref="PromptActivityEvent.DurationMilliseconds"/>
    /// or from <see cref="PromptActivityEvent.ResponseCompletedAtUtc"/> − timestamp when positive.
    /// </summary>
    public static long ResolveMilliseconds(PromptActivityEvent activityEvent)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);

        if (activityEvent.DurationMilliseconds is long ms && ms > 0)
        {
            return ms;
        }

        if (activityEvent.ResponseCompletedAtUtc is DateTimeOffset completed)
        {
            var delta = (long)(completed.ToUniversalTime() - activityEvent.TimestampUtc.ToUniversalTime())
                .TotalMilliseconds;
            return delta > 0 ? delta : 0;
        }

        return 0;
    }
}
