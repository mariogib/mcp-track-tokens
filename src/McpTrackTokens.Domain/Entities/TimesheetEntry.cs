using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A manual timesheet entry for a project (start/end + notes).
/// </summary>
public sealed class TimesheetEntry : EntityBase, IAuditable
{
    /// <summary>
    /// Gets or sets the owning project identifier.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the timesheet category identifier.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Gets or sets when the timesheet entry started (UTC).
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the timesheet entry ended (UTC), if ended.
    /// </summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets free-form notes for the entry.
    /// </summary>
    public string? Notes { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Creates a new open timesheet entry.
    /// </summary>
    public static TimesheetEntry Start(
        Guid projectId,
        Guid categoryId,
        DateTimeOffset startedAtUtc,
        string? notes = null,
        Guid? id = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ValidationException(nameof(ProjectId), "ProjectId is required.");
        }

        if (categoryId == Guid.Empty)
        {
            throw new ValidationException(nameof(CategoryId), "CategoryId is required.");
        }

        var started = startedAtUtc.ToUniversalTime();
        return new TimesheetEntry(id ?? Guid.NewGuid(), started)
        {
            ProjectId = projectId,
            CategoryId = categoryId,
            StartedAtUtc = started,
            Notes = NormalizeNotes(notes),
            UpdatedAtUtc = started
        };
    }

    /// <summary>
    /// Ends the timesheet entry.
    /// </summary>
    public void End(DateTimeOffset endedAtUtc, string? appendNotes = null)
    {
        var ended = endedAtUtc.ToUniversalTime();
        if (ended < StartedAtUtc)
        {
            throw new ValidationException(
                nameof(EndedAtUtc),
                "EndedAtUtc cannot be earlier than StartedAtUtc.");
        }

        EndedAtUtc = ended;
        if (!string.IsNullOrWhiteSpace(appendNotes))
        {
            AppendNotes(appendNotes);
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Appends text to the existing notes (separated by a blank line when needed).
    /// </summary>
    public void AppendNotes(string text)
    {
        var addition = text.Trim();
        if (addition.Length == 0)
        {
            return;
        }

        Notes = string.IsNullOrWhiteSpace(Notes)
            ? addition
            : $"{Notes.TrimEnd()}\n\n{addition}";
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Applies a full administrative edit from the dashboard.
    /// </summary>
    public void ApplyAdminEdit(
        Guid categoryId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        string? notes)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ValidationException(nameof(CategoryId), "CategoryId is required.");
        }

        var started = startedAtUtc.ToUniversalTime();
        DateTimeOffset? ended = endedAtUtc?.ToUniversalTime();
        if (ended is DateTimeOffset end && end < started)
        {
            throw new ValidationException(
                nameof(EndedAtUtc),
                "EndedAtUtc cannot be earlier than StartedAtUtc.");
        }

        CategoryId = categoryId;
        StartedAtUtc = started;
        EndedAtUtc = ended;
        Notes = NormalizeNotes(notes);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimesheetEntry"/> class.
    /// </summary>
    public TimesheetEntry()
    {
        var now = DateTimeOffset.UtcNow;
        StartedAtUtc = now;
        UpdatedAtUtc = now;
    }

    private TimesheetEntry(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }

    private static string? NormalizeNotes(string? notes)
        => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
