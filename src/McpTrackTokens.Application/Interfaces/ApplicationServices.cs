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
/// Dashboard admin CRUD for editor sessions.
/// </summary>
public interface ISessionManagementService
{
    Task<EditorSession> CreateForProjectAsync(
        Guid projectId,
        CreateProjectSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<EditorSession> UpdateAsync(
        Guid sessionId,
        UpdateSessionRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Settings CRUD for timesheet categories.
/// </summary>
public interface ITimesheetCategoryService
{
    Task<IReadOnlyList<TimesheetCategoryDto>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<TimesheetCategoryDto> CreateAsync(
        CreateTimesheetCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<TimesheetCategoryDto> UpdateAsync(
        Guid id,
        UpdateTimesheetCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manual timesheet start/end and dashboard CRUD.
/// </summary>
public interface ITimesheetManagementService
{
    Task<TimesheetEntryDto> StartAsync(
        StartTimesheetRequest request,
        CancellationToken cancellationToken = default);

    Task<TimesheetEntryDto> EndAsync(
        EndTimesheetRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetEntryDto>> ListForProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists timesheet entries across projects for the dashboard.
    /// </summary>
    Task<IReadOnlyList<TimesheetEntryDto>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged timesheet list for dashboard browse (SQL OFFSET/LIMIT).
    /// </summary>
    Task<PagedResultDto<TimesheetEntryDto>> ListPagedAsync(
        TimesheetEntryPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<TimesheetEntryDto> CreateForProjectAsync(
        Guid projectId,
        CreateTimesheetEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<TimesheetEntryDto> UpdateAsync(
        Guid entryId,
        UpdateTimesheetEntryRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// When a new editor session is created: if the project has no open timesheet for the
    /// current local calendar day, closes every other open timesheet (notes append
    /// <c>autoclosed</c> or <c>day-boundary</c>) and creates one for this project
    /// (notes = <c>autocreated</c>).
    /// Does not call <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    Task EnsureAutocreatedOpenEntryAsync(
        Guid projectId,
        DateTimeOffset? startedAtUtc = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds timesheet duration reports for clients, projects, and overall rollups.
/// </summary>
public interface ITimesheetReportService
{
    Task<TimesheetOverallReport> GetOverallReportAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<TimesheetProjectReport> GetProjectReportAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<TimesheetClientReport> GetClientReportAsync(
        string clientName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists UTC months that contain timesheet entries, optionally scoped.
    /// </summary>
    Task<IReadOnlyList<TimesheetMonthAvailabilityDto>> ListMonthsWithEntriesAsync(
        Guid? projectId = null,
        string? clientName = null,
        CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Estimated token cost for a client using Settings rate-card prices × attributed tokens.
    /// </summary>
    Task<ClientTokenCostEstimate> GetClientTokenCostEstimateAsync(
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
    /// <summary>
    /// Builds the export payload in memory (no disk write).
    /// </summary>
    Task<ExportFileDto> BuildFileAsync(ExportRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the export and writes it under an approved export directory.
    /// </summary>
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

/// <summary>
/// Backs up and restores the local SQLite tracking database.
/// </summary>
public interface IDatabaseBackupService
{
    DatabaseBackupInfoDto GetInfo(string? destinationDirectory = null);

    Task<DatabaseBackupResultDto> BackupAsync(
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a temporary backup file for download. Caller must dispose the stream
    /// (DeleteOnClose removes the temp file).
    /// </summary>
    Task<(Stream Stream, string FileName)> CreateDownloadableBackupAsync(
        CancellationToken cancellationToken = default);

    Task<DatabaseRestoreResultDto> RestoreAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Checks whether Cursor hooks are installed and use event names compatible with
/// the installed Cursor version.
/// </summary>
public interface ICursorHooksCompatibilityService
{
    /// <summary>
    /// Inspects Cursor install metadata, <c>~/.cursor/hooks.json</c>, installed hook
    /// scripts, and recent Cursor ingest activity.
    /// </summary>
    /// <param name="cursorUserDirectory">
    /// Optional override for the Cursor user config directory (default <c>~/.cursor</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CursorHooksCompatibilityReportDto> CheckAsync(
        string? cursorUserDirectory = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Replays offline queued hook events from disk into the ingest pipeline.
/// </summary>
public interface IOfflineQueueReplayService
{
    Task<OfflineQueueReplayResultDto> ReplayAsync(CancellationToken cancellationToken = default);
}
