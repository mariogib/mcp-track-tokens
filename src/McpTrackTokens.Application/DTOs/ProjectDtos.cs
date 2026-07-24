namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Summary view of a tracked project.
/// </summary>
public sealed record ProjectDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? ClientName { get; init; }

    public string? BillingCode { get; init; }

    public string Currency { get; init; } = "USD";

    public string? PrimaryRepositoryPath { get; init; }

    public string? PrimaryRemoteUrl { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int RepositoryCount { get; init; }

    public DateTimeOffset? LastActivityAtUtc { get; init; }
}

/// <summary>
/// Detailed project view including repositories and aliases.
/// </summary>
public sealed record ProjectDetailDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? ClientName { get; init; }

    public string? BillingCode { get; init; }

    public string Currency { get; init; } = "USD";

    public string? PrimaryRepositoryPath { get; init; }

    public string? PrimaryRemoteUrl { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<ProjectRepositoryDto> Repositories { get; init; } = [];

    public IReadOnlyList<ProjectAliasDto> Aliases { get; init; } = [];

    public ActivitySummaryDto? Activity { get; init; }

    public UsageSummaryDto? Usage { get; init; }

    public CostSummaryDto? Cost { get; init; }
}

/// <summary>
/// Repository mapping for a project.
/// </summary>
public sealed record ProjectRepositoryDto
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string LocalPath { get; init; } = string.Empty;

    public string NormalizedPath { get; init; } = string.Empty;

    public string? RemoteUrl { get; init; }

    public string? NormalizedRemoteUrl { get; init; }

    public string? DefaultBranch { get; init; }

    public bool IsActive { get; init; }
}

/// <summary>
/// Alias mapping for a project.
/// </summary>
public sealed record ProjectAliasDto
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Alias { get; init; } = string.Empty;

    public string NormalizedAlias { get; init; } = string.Empty;

    public string AliasType { get; init; } = string.Empty;
}

/// <summary>
/// Request to create a project.
/// </summary>
public sealed record CreateProjectRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public string? ClientName { get; init; }

    public string? BillingCode { get; init; }

    public string? Currency { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public IReadOnlyList<string>? Aliases { get; init; }
}

/// <summary>
/// Request to update an existing project.
/// </summary>
public sealed record UpdateProjectRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Slug { get; init; }

    public string? ClientName { get; init; }

    public string? BillingCode { get; init; }

    public string? Currency { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public bool? IsActive { get; init; }
}

/// <summary>
/// Activity metrics for a project or period.
/// </summary>
public sealed record ActivitySummaryDto
{
    public int PromptCount { get; init; }

    public int AgentRuns { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public long ActiveProjectTimeSeconds { get; init; }

    public int SessionCount { get; init; }

    public int FailureCount { get; init; }

    public int CancellationCount { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }
}

/// <summary>
/// Imported usage metrics.
/// </summary>
public sealed record UsageSummaryDto
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long TotalTokens { get; init; }

    public int RequestCount { get; init; }

    public decimal ReportedCost { get; init; }

    public string Currency { get; init; } = "USD";

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }
}

/// <summary>
/// Cost summary separating usage-based and subscription allocation.
/// </summary>
public sealed record CostSummaryDto
{
    public decimal UsageBasedCost { get; init; }

    public decimal SubscriptionAllocation { get; init; }

    public decimal OtherProviderCost { get; init; }

    public decimal UnallocatedCost { get; init; }

    public decimal TotalAiCost { get; init; }

    /// <summary>Rate-card calculated cost (Settings Cursor token rates × attributed tokens).</summary>
    public decimal CalculatedTokenCost { get; init; }

    public string Currency { get; init; } = "USD";

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }
}

/// <summary>
/// Current tracking status snapshot.
/// </summary>
public sealed record TrackingStatusDto
{
    public bool IsHealthy { get; init; }

    public string DatabasePath { get; init; } = string.Empty;

    public string? DatabaseProvider { get; init; }

    public ProjectDto? CurrentProject { get; init; }

    public Guid? ActiveSessionId { get; init; }

    public string? ActiveSessionEditor { get; init; }

    public DateTimeOffset? LastEventAtUtc { get; init; }

    public string? LastEventType { get; init; }

    public int QueuedEventCount { get; init; }

    public int UnallocatedEventCount { get; init; }

    public int UnallocatedUsageCount { get; init; }

    public DateTimeOffset? LastCursorImportAtUtc { get; init; }

    public string? LastCursorImportStatus { get; init; }
}

/// <summary>
/// An unallocated activity or usage item awaiting attribution.
/// </summary>
public sealed record UnallocatedItemDto
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; }

    public string? Editor { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? ExternalRequestId { get; init; }

    public long? TotalTokens { get; init; }

    public decimal? ReportedCost { get; init; }

    /// <summary>Rate-card calculated cost for this usage row.</summary>
    public decimal CalculatedTokenCost { get; init; }

    public string? Currency { get; init; }

    public string? SuggestedProjectName { get; init; }

    public Guid? SuggestedProjectId { get; init; }

    public string? SuggestedMethod { get; init; }

    public string? SuggestedConfidence { get; init; }

    public string? Reason { get; init; }

    public string? WorkspacePath { get; init; }

    public string? EventType { get; init; }

    public long? DurationMilliseconds { get; init; }
}

/// <summary>
/// Request to assign unallocated activity events to a project.
/// </summary>
public sealed record AssignActivityRequestDto
{
    public Guid ProjectId { get; init; }

    public IReadOnlyList<Guid> EventIds { get; init; } = [];
}

/// <summary>
/// Result of assigning activity events to a project.
/// </summary>
public sealed record AssignActivityResultDto
{
    public Guid ProjectId { get; init; }

    public int Assigned { get; init; }
}

/// <summary>
/// Request to delete unallocated activity events by id.
/// </summary>
public sealed record DeleteActivityRequestDto
{
    public IReadOnlyList<Guid> EventIds { get; init; } = [];
}

/// <summary>
/// Result of deleting unallocated activity events.
/// </summary>
public sealed record DeleteActivityResultDto
{
    public int Deleted { get; init; }
}
