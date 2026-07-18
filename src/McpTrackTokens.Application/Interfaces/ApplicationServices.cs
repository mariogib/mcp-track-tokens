using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Ingests editor activity events idempotently.
/// </summary>
public interface IEventIngestionService
{
    Task<IngestEventResultDto> IngestAsync(IngestEventDto dto, CancellationToken cancellationToken = default);

    Task<BatchIngestResultDto> IngestBatchAsync(BatchIngestRequestDto request, CancellationToken cancellationToken = default);

    Task<EditorSession> StartSessionAsync(SessionStartDto dto, CancellationToken cancellationToken = default);

    Task<EditorSession?> EndSessionAsync(SessionEndDto dto, CancellationToken cancellationToken = default);

    Task<EditorSession?> HeartbeatAsync(HeartbeatDto dto, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calculates and persists activity windows.
/// </summary>
public interface IActivityWindowService
{
    Task<RecalculateWindowsResultDto> RecalculateAsync(
        Guid? projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? inactivityThresholdMinutes = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    Task UpdateForEventAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default);

    IReadOnlyList<ActivityWindow> MergeOverlappingSameProjectWindows(IEnumerable<ActivityWindow> windows);
}

/// <summary>
/// Deterministic usage attribution engine.
/// </summary>
public interface IAttributionEngine
{
    /// <summary>
    /// Proposes attributions without persisting them.
    /// </summary>
    Task<IReadOnlyList<UsageAttribution>> ProposeAsync(
        ExternalUsageRecord usageRecord,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposes and persists attributions for a usage record.
    /// </summary>
    Task<IReadOnlyList<UsageAttribution>> AttributeAsync(
        ExternalUsageRecord usageRecord,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a previously proposed attribution set for a usage record.
    /// </summary>
    Task PersistAsync(
        Guid externalUsageRecordId,
        IReadOnlyList<UsageAttribution> attributions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageAttribution>> AttributeManualAsync(
        AllocationRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Allocates fixed subscription cost separately from usage-based cost.
/// </summary>
public interface ISubscriptionAllocationService
{
    Task<IReadOnlyList<ProjectAllocationShareDto>> AllocateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        AllocationRuleType? method = null,
        decimal? amount = null,
        string? currency = null,
        IReadOnlyDictionary<Guid, decimal>? manualPercentages = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Detects or creates projects from repository context.
/// </summary>
public interface IProjectDetectionService
{
    Task<Project?> DetectAsync(
        string? workspacePath,
        string? repositoryPath,
        string? remoteUrl,
        string? activeFilePath = null,
        bool? createIfMissing = null,
        CancellationToken cancellationToken = default);

    Task<ProjectDetailDto> RegisterAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDetailDto> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds deterministic report DTOs.
/// </summary>
public interface IReportService
{
    Task<DailyActivityReport> GetDailyActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<ProjectActivityReport> GetProjectActivityAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<ProjectCostReport> GetProjectCostAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool includeSubscriptionAllocation = true,
        CancellationToken cancellationToken = default);

    Task<ProjectTokenCostEstimate> GetProjectTokenCostEstimateAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<UsageSummaryDto> GetProjectUsageSummaryAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<ClientCostReport> GetClientCostAsync(
        string clientName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<UsageAttributionReport> GetUsageAttributionAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<UnallocatedUsageReport> GetUnallocatedUsageAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<ImportedUsageReport> GetImportedUsageAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<MonthlySummaryReport> GetMonthlySummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task<EditorComparisonReport> GetEditorComparisonAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<ModelCostReport> GetModelCostAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<TrackingStatusDto> GetTrackingStatusAsync(CancellationToken cancellationToken = default);

    Task<ActivitySummaryDto> GetActivitySummaryAsync(
        Guid? projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnallocatedItemDto>> GetUnallocatedActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs attribution over a date range.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationResultDto> RunAsync(
        ReconciliationRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exports reports with path-traversal protection.
/// </summary>
public interface IExportService
{
    Task<ExportResultDto> ExportAsync(ExportRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates and verifies hashed tracking API keys.
/// </summary>
public interface IApiKeyService
{
    Task<ApiKeyCreateResultDto> CreateAsync(CreateApiKeyRequestDto request, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string plaintextKey, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackingApiKey>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
}
