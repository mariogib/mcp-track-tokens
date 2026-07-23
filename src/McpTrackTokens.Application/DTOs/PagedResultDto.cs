namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Page of results with total count for browse/lazy loading.
/// </summary>
public sealed record PagedResultDto<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }
}

/// <summary>
/// Distinct filter values for project prompts in a date range.
/// </summary>
public sealed record PromptFacetsDto
{
    public IReadOnlyList<string> Models { get; init; } = [];

    public IReadOnlyList<string> Branches { get; init; } = [];

    public IReadOnlyList<string> EventTypes { get; init; } = [];

    /// <summary>
    /// Distinct UTC calendar days (<c>yyyy-MM-dd</c>) present in the range.
    /// </summary>
    public IReadOnlyList<string> Days { get; init; } = [];
}

/// <summary>
/// Browse filters for paged activity / prompt lists.
/// </summary>
public sealed record ActivityEventPageFilter
{
    public Guid? ProjectId { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string? Search { get; init; }

    public string? Status { get; init; }

    public string? EventType { get; init; }

    public string? Model { get; init; }

    public string? Branch { get; init; }

    /// <summary>
    /// When true, only <c>PromptSubmitted</c> rows (dashboard prompts tab).
    /// </summary>
    public bool PromptSubmittedOnly { get; init; }
}

/// <summary>
/// Browse filters for paged timesheet entry lists.
/// </summary>
public sealed record TimesheetEntryPageFilter
{
    public Guid? ProjectId { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public string? Search { get; init; }

    /// <summary>
    /// <c>open</c>, <c>closed</c>, or null for all.
    /// </summary>
    public string? OpenClosed { get; init; }
}
