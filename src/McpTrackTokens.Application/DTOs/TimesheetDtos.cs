namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Timesheet entry response.
/// </summary>
public sealed record TimesheetEntryDto
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

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
    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Update an existing timesheet entry (dashboard).
/// </summary>
public sealed record UpdateTimesheetEntryRequest
{
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
