using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// A timestamped activity used as input to <see cref="ActivityWindowCalculator"/>.
/// </summary>
/// <param name="TimestampUtc">Activity timestamp in UTC.</param>
/// <param name="EventType">Type of activity event.</param>
public readonly record struct ActivityTimestamp(
    DateTimeOffset TimestampUtc,
    ActivityEventType EventType);

/// <summary>
/// A calculated activity window before persistence.
/// </summary>
/// <param name="StartedAtUtc">Window start in UTC.</param>
/// <param name="EndedAtUtc">Window end in UTC (last activity + threshold).</param>
/// <param name="LastActivityAtUtc">Last activity that contributed to the window.</param>
/// <param name="InactivityThresholdMinutes">Threshold used for the calculation.</param>
/// <param name="CalculationVersion">Algorithm version.</param>
public sealed record CalculatedActivityWindow(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    int InactivityThresholdMinutes,
    string CalculationVersion)
{
    /// <summary>
    /// Gets the duration of the window.
    /// </summary>
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;

    /// <summary>
    /// Gets the duration in whole seconds.
    /// </summary>
    public long DurationSeconds => (long)Math.Round(Duration.TotalSeconds, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Calculates active project time windows from activity events using an inactivity threshold.
/// </summary>
/// <remarks>
/// Rules (version <see cref="CalculationVersion"/>):
/// <list type="number">
/// <item>A <see cref="ActivityEventType.PromptSubmitted"/> begins an activity window.</item>
/// <item>Agent (and other extending) events extend the current window.</item>
/// <item>A new prompt inside the inactivity threshold extends the same window.</item>
/// <item>A prompt after the threshold begins a new window.</item>
/// <item>Window end = last activity + threshold minutes.</item>
/// </list>
/// Example with a 15-minute threshold:
/// 09:00 prompt, 09:08 prompt, 09:14 agent completed, 09:31 prompt
/// yields Window1 09:00–09:29 and Window2 09:31–09:46.
/// </remarks>
public sealed class ActivityWindowCalculator
{
    /// <summary>
    /// Current calculation algorithm version.
    /// </summary>
    public const string CalculationVersion = "1.0";

    /// <summary>
    /// Default inactivity threshold in minutes.
    /// </summary>
    public const int DefaultInactivityThresholdMinutes = 15;

    /// <summary>
    /// Calculates activity windows from a sequence of activity timestamps.
    /// </summary>
    /// <param name="activities">Activity events (any order; sorted internally).</param>
    /// <param name="thresholdMinutes">Inactivity threshold in minutes.</param>
    /// <returns>Calculated windows in chronological order.</returns>
    public IReadOnlyList<CalculatedActivityWindow> Calculate(
        IEnumerable<ActivityTimestamp> activities,
        int thresholdMinutes = DefaultInactivityThresholdMinutes)
    {
        Guard.AgainstNull(activities);
        Guard.AgainstZeroOrNegative(thresholdMinutes);

        var ordered = activities
            .Where(a => IsWindowRelevant(a.EventType))
            .OrderBy(a => a.TimestampUtc)
            .ThenBy(a => a.EventType)
            .ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<CalculatedActivityWindow>();
        }

        var threshold = TimeSpan.FromMinutes(thresholdMinutes);
        var windows = new List<CalculatedActivityWindow>();

        DateTimeOffset? windowStart = null;
        DateTimeOffset lastActivity = default;

        foreach (var activity in ordered)
        {
            var at = activity.TimestampUtc.ToUniversalTime();

            if (windowStart is null)
            {
                // A prompt begins a window; other relevant events may also open one if they appear first.
                if (!CanBeginWindow(activity.EventType) && !CanExtendWindow(activity.EventType))
                {
                    continue;
                }

                windowStart = at;
                lastActivity = at;
                continue;
            }

            var gap = at - lastActivity;
            if (gap > threshold)
            {
                windows.Add(CreateWindow(windowStart.Value, lastActivity, thresholdMinutes));
                windowStart = at;
                lastActivity = at;
                continue;
            }

            // Within threshold: extend the same window.
            if (at > lastActivity)
            {
                lastActivity = at;
            }
        }

        if (windowStart is not null)
        {
            windows.Add(CreateWindow(windowStart.Value, lastActivity, thresholdMinutes));
        }

        return windows;
    }

    /// <summary>
    /// Returns whether the event type can begin an activity window.
    /// </summary>
    public static bool CanBeginWindow(ActivityEventType eventType)
        => eventType == ActivityEventType.PromptSubmitted;

    /// <summary>
    /// Returns whether the event type extends an open activity window.
    /// </summary>
    public static bool CanExtendWindow(ActivityEventType eventType)
        => eventType is ActivityEventType.PromptSubmitted
            or ActivityEventType.AgentStarted
            or ActivityEventType.AgentCompleted
            or ActivityEventType.AgentCancelled
            or ActivityEventType.AgentFailed
            or ActivityEventType.ToolStarted
            or ActivityEventType.ToolCompleted
            or ActivityEventType.Heartbeat;

    /// <summary>
    /// Returns whether the event participates in window calculation.
    /// </summary>
    public static bool IsWindowRelevant(ActivityEventType eventType)
        => CanBeginWindow(eventType) || CanExtendWindow(eventType);

    private static CalculatedActivityWindow CreateWindow(
        DateTimeOffset startedAtUtc,
        DateTimeOffset lastActivityAtUtc,
        int thresholdMinutes)
    {
        var endedAtUtc = lastActivityAtUtc.AddMinutes(thresholdMinutes);
        return new CalculatedActivityWindow(
            startedAtUtc,
            endedAtUtc,
            lastActivityAtUtc,
            thresholdMinutes,
            CalculationVersion);
    }
}
