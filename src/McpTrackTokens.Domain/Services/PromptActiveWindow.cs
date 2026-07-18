using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// Second-precision active interval for a prompt: from start through start + duration.
/// </summary>
public static class PromptActiveWindow
{
    /// <summary>
    /// Maximum lookback when scanning for prompts that might still be active at a usage time.
    /// </summary>
    public static readonly TimeSpan MaxLookback = TimeSpan.FromHours(24);

    /// <summary>
    /// Resolves the inclusive second-precision window
    /// [<paramref name="startSecond"/>, <paramref name="endSecond"/>].
    /// Duration comes from <see cref="PromptActivityEvent.DurationMilliseconds"/>,
    /// else from <see cref="PromptActivityEvent.ResponseCompletedAtUtc"/>, else zero
    /// (usage must match the prompt second exactly).
    /// </summary>
    public static void GetWindow(
        PromptActivityEvent prompt,
        out DateTimeOffset startSecond,
        out DateTimeOffset endSecond)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        startSecond = TimestampPrecision.RoundToSecond(prompt.TimestampUtc);
        var durationSeconds = ResolveDurationSeconds(prompt, startSecond);
        endSecond = startSecond.AddSeconds(durationSeconds);
    }

    /// <summary>
    /// Returns whether <paramref name="usageUtc"/> (rounded to the second) falls in the prompt window.
    /// </summary>
    public static bool Contains(PromptActivityEvent prompt, DateTimeOffset usageUtc)
    {
        GetWindow(prompt, out var start, out var end);
        var at = TimestampPrecision.RoundToSecond(usageUtc);
        return at >= start && at <= end;
    }

    private static long ResolveDurationSeconds(PromptActivityEvent prompt, DateTimeOffset startSecond)
    {
        if (prompt.DurationMilliseconds is long ms && ms > 0)
        {
            return (long)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero);
        }

        if (prompt.ResponseCompletedAtUtc is DateTimeOffset completed)
        {
            var end = TimestampPrecision.RoundToSecond(completed);
            var seconds = (long)(end - startSecond).TotalSeconds;
            return Math.Max(0, seconds);
        }

        return 0;
    }
}
