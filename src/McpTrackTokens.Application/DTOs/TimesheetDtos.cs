namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Timesheet entry response.
/// </summary>
public sealed record TimesheetEntryDto
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string? Notes { get; init; }

    public bool IsOpen { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// Create a timesheet entry for a project (dashboard).
/// </summary>
public sealed record CreateTimesheetEntryRequest
{
    public Guid? CategoryId { get; init; }

    /// <summary>
    /// Optional category name (resolved case-insensitively when <see cref="CategoryId"/> is omitted).
    /// </summary>
    public string? Category { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Update an existing timesheet entry (dashboard).
/// </summary>
public sealed record UpdateTimesheetEntryRequest
{
    public Guid CategoryId { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Start an open timesheet entry (MCP / API).
/// </summary>
public sealed record StartTimesheetRequest
{
    public Guid? ProjectId { get; init; }

    public Guid? CategoryId { get; init; }

    /// <summary>
    /// Optional category name (resolved case-insensitively when <see cref="CategoryId"/> is omitted).
    /// Defaults to Work.
    /// </summary>
    public string? Category { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? ActiveFilePath { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// End the open timesheet entry for a project (MCP / API).
/// </summary>
public sealed record EndTimesheetRequest
{
    public Guid? ProjectId { get; init; }

    public Guid? TimesheetEntryId { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? ActiveFilePath { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    /// <summary>
    /// Optional note appended to the existing notes.
    /// </summary>
    public string? AppendNotes { get; init; }
}

/// <summary>
/// Summary totals for a timesheet report.
/// </summary>
public sealed record TimesheetReportTotals
{
    public long TotalDurationSeconds { get; init; }

    public int EntryCount { get; init; }

    public int OpenEntryCount { get; init; }
}

/// <summary>
/// A UTC calendar month that contains timesheet entries.
/// </summary>
public sealed record TimesheetMonthAvailabilityDto
{
    public int Year { get; init; }

    public int Month { get; init; }

    public int EntryCount { get; init; }
}

/// <summary>
/// Timesheet duration rolled up by category.
/// </summary>
public sealed record TimesheetCategoryBreakdownRow
{
    public Guid CategoryId { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public long DurationSeconds { get; init; }

    public int EntryCount { get; init; }
}

/// <summary>
/// Timesheet duration rolled up by project.
/// </summary>
public sealed record TimesheetProjectBreakdownRow
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string? ClientName { get; init; }

    public long DurationSeconds { get; init; }

    public int EntryCount { get; init; }
}

/// <summary>
/// Timesheet duration rolled up by client.
/// </summary>
public sealed record TimesheetClientBreakdownRow
{
    public string ClientName { get; init; } = string.Empty;

    public long DurationSeconds { get; init; }

    public int EntryCount { get; init; }

    public int ProjectCount { get; init; }
}

/// <summary>
/// Timesheet duration rolled up by UTC day.
/// </summary>
public sealed record TimesheetDailyBreakdownRow
{
    public DateOnly Day { get; init; }

    public long DurationSeconds { get; init; }

    public int EntryCount { get; init; }
}

/// <summary>
/// Timesheet report across all projects.
/// </summary>
public sealed record TimesheetOverallReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public TimesheetReportTotals Totals { get; init; } = new();

    public IReadOnlyList<TimesheetCategoryBreakdownRow> ByCategory { get; init; } = [];

    public IReadOnlyList<TimesheetProjectBreakdownRow> ByProject { get; init; } = [];

    public IReadOnlyList<TimesheetClientBreakdownRow> ByClient { get; init; } = [];

    public IReadOnlyList<TimesheetDailyBreakdownRow> ByDay { get; init; } = [];
}

/// <summary>
/// Timesheet report for one project.
/// </summary>
public sealed record TimesheetProjectReport
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string? ClientName { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public TimesheetReportTotals Totals { get; init; } = new();

    public IReadOnlyList<TimesheetCategoryBreakdownRow> ByCategory { get; init; } = [];

    public IReadOnlyList<TimesheetDailyBreakdownRow> ByDay { get; init; } = [];
}

/// <summary>
/// Timesheet report for one client (all matching projects).
/// </summary>
public sealed record TimesheetClientReport
{
    public string ClientName { get; init; } = string.Empty;

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public TimesheetReportTotals Totals { get; init; } = new();

    public IReadOnlyList<TimesheetProjectBreakdownRow> ByProject { get; init; } = [];

    public IReadOnlyList<TimesheetCategoryBreakdownRow> ByCategory { get; init; } = [];

    public IReadOnlyList<TimesheetDailyBreakdownRow> ByDay { get; init; } = [];
}
