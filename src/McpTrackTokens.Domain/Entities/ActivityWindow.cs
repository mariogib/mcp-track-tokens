using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A calculated span of active project time derived from activity events.
/// </summary>
public sealed class ActivityWindow : EntityBase
{
    /// <summary>
    /// Gets or sets the associated project identifier, when known.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the editor session identifier, when known.
    /// </summary>
    public Guid? EditorSessionId { get; set; }

    /// <summary>
    /// Gets or sets the window start time in UTC.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the window end time in UTC.
    /// </summary>
    public DateTimeOffset EndedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the duration in seconds.
    /// </summary>
    public long DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the inactivity threshold in minutes used to calculate the window.
    /// </summary>
    public int InactivityThresholdMinutes { get; set; }

    /// <summary>
    /// Gets or sets the calculation algorithm version.
    /// </summary>
    public string CalculationVersion { get; set; } = ActivityWindowCalculator.CalculationVersion;

    /// <summary>
    /// Creates an activity window and derives duration from the start/end timestamps.
    /// </summary>
    public static ActivityWindow Create(
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        int inactivityThresholdMinutes,
        Guid? projectId = null,
        Guid? editorSessionId = null,
        string? calculationVersion = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        Guard.AgainstZeroOrNegative(inactivityThresholdMinutes);
        var start = startedAtUtc.ToUniversalTime();
        var end = endedAtUtc.ToUniversalTime();
        Guard.Against(end < start, "EndedAtUtc cannot be earlier than StartedAtUtc.", nameof(endedAtUtc));

        var duration = (long)Math.Round((end - start).TotalSeconds, MidpointRounding.AwayFromZero);
        return new ActivityWindow(id ?? Guid.NewGuid(), createdAtUtc ?? DateTimeOffset.UtcNow)
        {
            ProjectId = projectId,
            EditorSessionId = editorSessionId,
            StartedAtUtc = start,
            EndedAtUtc = end,
            DurationSeconds = Guard.AgainstNegative(duration),
            InactivityThresholdMinutes = inactivityThresholdMinutes,
            CalculationVersion = string.IsNullOrWhiteSpace(calculationVersion)
                ? ActivityWindowCalculator.CalculationVersion
                : calculationVersion
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWindow"/> class.
    /// </summary>
    public ActivityWindow()
    {
    }

    private ActivityWindow(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
