namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Timesheet category response.
/// </summary>
public sealed record TimesheetCategoryDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// Create a timesheet category.
/// </summary>
public sealed record CreateTimesheetCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    public int? SortOrder { get; init; }
}

/// <summary>
/// Update a timesheet category.
/// </summary>
public sealed record UpdateTimesheetCategoryRequest
{
    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;
}
