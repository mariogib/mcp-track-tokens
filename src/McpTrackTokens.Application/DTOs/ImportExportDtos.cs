using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Preview of a Cursor usage import before persistence.
/// </summary>
public sealed record ImportPreviewDto
{
    public string FileName { get; init; } = string.Empty;

    public string? FileHash { get; init; }

    public string DetectedFormat { get; init; } = string.Empty;

    public UsageSource Source { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyDictionary<string, string> ColumnMappings { get; init; } =
        new Dictionary<string, string>();

    public int ReceivedCount { get; init; }

    public int ValidCount { get; init; }

    public int DuplicateCount { get; init; }

    public int InvalidCount { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<NormalizedUsageRecordDto> SampleRecords { get; init; } = [];
}

/// <summary>
/// Result of a completed (or dry-run) import.
/// </summary>
public sealed record ImportResultDto
{
    public Guid? ImportBatchId { get; init; }

    public bool DryRun { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string? FileHash { get; init; }

    public UsageSource Source { get; init; }

    public ImportStatus Status { get; init; }

    public int ReceivedCount { get; init; }

    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int FailedCount { get; init; }

    public string? ErrorSummary { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }
}

/// <summary>
/// Normalized usage record used during import preview.
/// </summary>
public sealed record NormalizedUsageRecordDto
{
    /// <summary>
    /// Stable external identity used for import deduplication. Set during parse when absent from the file.
    /// </summary>
    public string? ExternalRecordId { get; set; }

    public DateTimeOffset TimestampUtc { get; init; }

    public DateTimeOffset? PeriodStartUtc { get; init; }

    public DateTimeOffset? PeriodEndUtc { get; init; }

    public string? UserIdentifier { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? CachedInputTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public long? TotalTokens { get; init; }

    public decimal? ReportedCost { get; init; }

    public string? Currency { get; init; }

    public int? RequestCount { get; init; }

    public string? ExternalSessionId { get; init; }

    public string? ExternalRequestId { get; init; }

    public string? ExternalConversationId { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public Guid? ExplicitProjectId { get; init; }

    public string? MetadataJson { get; init; }
}

/// <summary>
/// Request to import Cursor usage from a file.
/// </summary>
public sealed record ImportCursorUsageRequestDto
{
    public string FilePath { get; init; } = string.Empty;

    public string? Format { get; init; }

    public string? Timezone { get; init; }

    public bool DryRun { get; init; }

    public bool Force { get; init; }

    public IReadOnlyDictionary<string, string>? ColumnMappings { get; init; }
}

/// <summary>
/// Manual or suggested allocation of usage to projects.
/// </summary>
public sealed record AllocationRequestDto
{
    public Guid UsageRecordId { get; init; }

    public IReadOnlyList<ProjectAllocationShareDto> ProjectAllocations { get; init; } = [];

    public string? Reason { get; init; }

    public string? ReviewedBy { get; init; }

    public bool ReplaceExisting { get; init; } = true;
}

/// <summary>
/// A single project share within an allocation request.
/// </summary>
public sealed record ProjectAllocationShareDto
{
    public Guid ProjectId { get; init; }

    public decimal Percentage { get; init; }

    public Guid? EditorSessionId { get; init; }

    public Guid? ActivityEventId { get; init; }
}

/// <summary>
/// Request to export a report to disk.
/// </summary>
public sealed record ExportRequestDto
{
    public string ReportType { get; init; } = string.Empty;

    public ExportFormat Format { get; init; } = ExportFormat.Json;

    public Guid? ProjectId { get; init; }

    public string? RepositoryPath { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public string? OutputDirectory { get; init; }

    public string? FileName { get; init; }

    public bool IncludeActivity { get; init; } = true;

    public bool IncludeUsage { get; init; } = true;

    public bool IncludeCosts { get; init; } = true;
}

/// <summary>
/// Result of an export operation written to disk.
/// </summary>
public sealed record ExportResultDto
{
    public string FilePath { get; init; } = string.Empty;

    public ExportFormat Format { get; init; }

    public long ByteCount { get; init; }

    public DateTimeOffset ExportedAtUtc { get; init; }
}

/// <summary>
/// In-memory export payload for HTTP download.
/// </summary>
public sealed record ExportFileDto
{
    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = "application/octet-stream";

    public byte[] Content { get; init; } = [];

    public ExportFormat Format { get; init; }

    public long ByteCount { get; init; }

    public DateTimeOffset ExportedAtUtc { get; init; }
}

/// <summary>
/// Result of creating a tracking API key (plaintext returned once).
/// </summary>
public sealed record ApiKeyCreateResultDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Plaintext API key. Shown only at creation time; never stored.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string? AllowedEditors { get; init; }

    public string? AllowedMachineNames { get; init; }
}

/// <summary>
/// Request to create an API key.
/// </summary>
public sealed record CreateApiKeyRequestDto
{
    public string Name { get; init; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string? AllowedEditors { get; init; }

    public string? AllowedMachineNames { get; init; }
}

/// <summary>
/// Request to run usage reconciliation.
/// </summary>
public sealed record ReconciliationRequestDto
{
    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public bool DryRun { get; init; }

    public bool IncludeLowConfidence { get; init; }
}

/// <summary>
/// Result of a reconciliation run.
/// </summary>
public sealed record ReconciliationResultDto
{
    public bool DryRun { get; init; }

    public DateTimeOffset FromUtc { get; init; }

    public DateTimeOffset ToUtc { get; init; }

    public int ProcessedCount { get; init; }

    public int AllocatedCount { get; init; }

    public int UnallocatedCount { get; init; }

    public int SkippedCount { get; init; }

    public IReadOnlyList<UsageAttributionRow> Attributions { get; init; } = [];

    /// <summary>
    /// Usage rows that could not be linked to a prior prompt (or otherwise stayed unallocated).
    /// </summary>
    public IReadOnlyList<UsageAttributionRow> Unallocated { get; init; } = [];
}

/// <summary>
/// Result of recalculating activity windows.
/// </summary>
public sealed record RecalculateWindowsResultDto
{
    public bool DryRun { get; init; }

    public Guid? ProjectId { get; init; }

    public int WindowCount { get; init; }

    public long TotalActiveSeconds { get; init; }

    public string CalculationVersion { get; init; } = "1.0";
}
