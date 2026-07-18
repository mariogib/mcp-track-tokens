namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Daily activity report across projects or for a single project.
/// </summary>
public sealed record DailyActivityReport
{
    public DateOnly Day { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public IReadOnlyList<DailyActivityRow> Rows { get; init; } = [];

    public ActivitySummaryDto Totals { get; init; } = new();
}

/// <summary>
/// One row in a daily activity report.
/// </summary>
public sealed record DailyActivityRow
{
    public DateOnly Day { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public string? Editor { get; init; }

    public int PromptCount { get; init; }

    public int AgentRuns { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public long ActiveProjectTimeSeconds { get; init; }

    public int SessionCount { get; init; }

    /// <summary>Imported usage tokens attributed to the project on this day.</summary>
    public long TotalTokens { get; init; }
}

/// <summary>
/// Project activity report for a date range.
/// </summary>
public sealed record ProjectActivityReport
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string ProjectSlug { get; init; } = string.Empty;

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public int PromptCount { get; init; }

    public int AgentRuns { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public long ActiveProjectTimeSeconds { get; init; }

    public int SessionCount { get; init; }

    public int FailureCount { get; init; }

    public int CancellationCount { get; init; }

    public IReadOnlyList<DailyActivityRow> ByDay { get; init; } = [];

    public IReadOnlyList<NamedMetricRow> ByEditor { get; init; } = [];

    public IReadOnlyList<NamedMetricRow> ByBranch { get; init; } = [];
}

/// <summary>
/// Named metric breakdown row.
/// </summary>
public sealed record NamedMetricRow
{
    public string Name { get; init; } = string.Empty;

    public int PromptCount { get; init; }

    public int AgentRuns { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public long ActiveProjectTimeSeconds { get; init; }

    public decimal UsageBasedCost { get; init; }

    public decimal SubscriptionAllocation { get; init; }
}

/// <summary>
/// Project cost report separating usage, subscription, and unallocated amounts.
/// </summary>
public sealed record ProjectCostReport
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string? ClientName { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string Currency { get; init; } = "USD";

    public long ActiveProjectTimeSeconds { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public int PromptCount { get; init; }

    public long ImportedTotalTokens { get; init; }

    public decimal UsageBasedCursorCost { get; init; }

    public decimal SubscriptionAllocation { get; init; }

    public decimal OtherProviderCost { get; init; }

    public decimal UnallocatedCost { get; init; }

    public decimal TotalAiCost { get; init; }

    public IReadOnlyList<NamedMetricRow> ByModel { get; init; } = [];
}

/// <summary>
/// Estimated project token cost using the configured Cursor rate card.
/// </summary>
public sealed record ProjectTokenCostEstimate
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string Currency { get; init; } = "USD";

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long TotalTokens { get; init; }

    public decimal EstimatedCost { get; init; }

    public decimal ReportedCost { get; init; }

    public int RateCardModelCount { get; init; }

    public bool HasRateCard { get; init; }

    public IReadOnlyList<TokenCostModelRow> ByModel { get; init; } = [];
}

/// <summary>
/// Per-model token cost estimate row.
/// </summary>
public sealed record TokenCostModelRow
{
    public string Model { get; init; } = string.Empty;

    public string RateSource { get; init; } = string.Empty;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long TotalTokens { get; init; }

    public decimal EstimatedCost { get; init; }

    public decimal ReportedCost { get; init; }

    public decimal InputPerMillion { get; init; }

    public decimal OutputPerMillion { get; init; }

    public decimal CacheReadPerMillion { get; init; }

    public decimal? ReasoningPerMillion { get; init; }
}

/// <summary>
/// Client-level cost rollup.
/// </summary>
public sealed record ClientCostReport
{
    public string ClientName { get; init; } = string.Empty;

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string Currency { get; init; } = "USD";

    public int ProjectCount { get; init; }

    public long ActiveProjectTimeSeconds { get; init; }

    public long AgentDurationMilliseconds { get; init; }

    public int PromptCount { get; init; }

    public decimal UsageBasedCost { get; init; }

    public decimal SubscriptionAllocation { get; init; }

    public decimal OtherProviderCost { get; init; }

    public decimal TotalAiCost { get; init; }

    public IReadOnlyList<ProjectCostReport> Projects { get; init; } = [];
}

/// <summary>
/// Usage attribution detail report.
/// </summary>
public sealed record UsageAttributionReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public IReadOnlyList<UsageAttributionRow> Rows { get; init; } = [];

    public decimal TotalAllocatedCost { get; init; }

    public decimal TotalUnallocatedCost { get; init; }

    public string Currency { get; init; } = "USD";
}

/// <summary>
/// One attribution row.
/// </summary>
public sealed record UsageAttributionRow
{
    public Guid UsageRecordId { get; init; }

    public Guid? AttributionId { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    /// <summary>Prompt activity event this usage row was linked to during reconciliation.</summary>
    public Guid? ActivityEventId { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public decimal AllocatedCost { get; init; }

    public decimal AllocationPercentage { get; init; }

    public long AllocatedTotalTokens { get; init; }

    public string AttributionMethod { get; init; } = string.Empty;

    public string Confidence { get; init; } = string.Empty;

    public string? Reason { get; init; }
}

/// <summary>
/// Unallocated usage report.
/// </summary>
public sealed record UnallocatedUsageReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public int Count { get; init; }

    public decimal TotalCost { get; init; }

    public string Currency { get; init; } = "USD";

    public IReadOnlyList<UnallocatedItemDto> Items { get; init; } = [];
}

/// <summary>
/// A single imported external usage row.
/// </summary>
public sealed record ImportedUsageItemDto
{
    public Guid Id { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    public string Source { get; init; } = string.Empty;

    public string? ExternalRecordId { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? CachedInputTokens { get; init; }

    public long TotalTokens { get; init; }

    public decimal ReportedCost { get; init; }

    public string Currency { get; init; } = "USD";

    public int? RequestCount { get; init; }

    public Guid? ImportBatchId { get; init; }

    public DateTimeOffset ImportedAtUtc { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public Guid? ActivityEventId { get; init; }

    public string? AttributionMethod { get; init; }
}

/// <summary>
/// All imported usage rows in a date range.
/// </summary>
public sealed record ImportedUsageReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public int Count { get; init; }

    public long TotalTokens { get; init; }

    public decimal TotalCost { get; init; }

    public string Currency { get; init; } = "USD";

    public IReadOnlyList<ImportedUsageItemDto> Items { get; init; } = [];
}

/// <summary>
/// Monthly summary across projects.
/// </summary>
public sealed record MonthlySummaryReport
{
    public int Year { get; init; }

    public int Month { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string Currency { get; init; } = "USD";

    public ActivitySummaryDto Activity { get; init; } = new();

    public UsageSummaryDto Usage { get; init; } = new();

    public CostSummaryDto Cost { get; init; } = new();

    public IReadOnlyList<ProjectCostReport> Projects { get; init; } = [];
}

/// <summary>
/// Editor comparison report.
/// </summary>
public sealed record EditorComparisonReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public IReadOnlyList<NamedMetricRow> Editors { get; init; } = [];
}

/// <summary>
/// Model cost breakdown report.
/// </summary>
public sealed record ModelCostReport
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string Currency { get; init; } = "USD";

    public IReadOnlyList<ModelCostRow> Models { get; init; } = [];
}

/// <summary>
/// Cost metrics for a single model.
/// </summary>
public sealed record ModelCostRow
{
    public string Model { get; init; } = string.Empty;

    public string? Provider { get; init; }

    public long TotalTokens { get; init; }

    public int RequestCount { get; init; }

    public decimal UsageBasedCost { get; init; }

    public decimal AllocatedCost { get; init; }

    public decimal UnallocatedCost { get; init; }
}
