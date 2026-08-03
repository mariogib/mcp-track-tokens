using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Persistence for projects, repositories, and aliases.
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListByClientAsync(string clientName, CancellationToken cancellationToken = default);

    Task<Project?> FindByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default);

    Task<Project?> FindByNormalizedRemoteUrlAsync(string normalizedRemoteUrl, CancellationToken cancellationToken = default);

    Task<Project?> FindByAliasAsync(string normalizedAlias, AliasType? aliasType = null, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);

    Task AddRepositoryAsync(ProjectRepository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the project's single repository mapping. Clears it when <paramref name="localPath"/> is empty.
    /// </summary>
    Task SetRepositoryAsync(
        Guid projectId,
        string? localPath,
        string? remoteUrl = null,
        CancellationToken cancellationToken = default);

    Task AddAliasAsync(ProjectAlias alias, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectRepository>> GetRepositoriesAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectAlias>> GetAliasesAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, Guid? excludingProjectId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for editor sessions.
/// </summary>
public interface ISessionRepository
{
    Task<EditorSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EditorSession?> GetByExternalSessionIdAsync(
        string externalSessionId,
        EditorType? editor = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recently active session for the editor + workspace, if any.
    /// Workspace paths are compared after <c>NormalizedPath</c> normalization.
    /// </summary>
    Task<EditorSession?> GetActiveForWorkspaceAsync(
        EditorType editor,
        string? workspacePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> GetActiveAtAsync(
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        SessionPageFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> ListPagedAsync(
        SessionPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(EditorSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(EditorSession session, CancellationToken cancellationToken = default);

    Task DeleteAsync(EditorSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates last-activity (and optionally assigns a project) without optimistic concurrency conflicts.
    /// </summary>
    Task TouchActivityAsync(
        Guid sessionId,
        DateTimeOffset activityAtUtc,
        Guid? assignProjectId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for timesheet categories.
/// </summary>
public interface ITimesheetCategoryRepository
{
    Task<TimesheetCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TimesheetCategory?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetCategory>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(TimesheetCategory category, CancellationToken cancellationToken = default);

    Task UpdateAsync(TimesheetCategory category, CancellationToken cancellationToken = default);

    Task DeleteAsync(TimesheetCategory category, CancellationToken cancellationToken = default);

    Task<int> CountEntriesAsync(Guid categoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for manual timesheet entries.
/// </summary>
public interface ITimesheetEntryRepository
{
    Task<TimesheetEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetEntry>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists timesheet entries across projects, optionally filtered by project and date range.
    /// </summary>
    Task<IReadOnlyList<TimesheetEntry>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts timesheet entries matching browse filters (SQL).
    /// </summary>
    Task<int> CountAsync(
        TimesheetEntryPageFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a page of timesheet entries with SQL OFFSET/LIMIT.
    /// </summary>
    Task<IReadOnlyList<TimesheetEntry>> ListPagedAsync(
        TimesheetEntryPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct calendar months (UTC) that contain at least one timesheet entry.
    /// </summary>
    Task<IReadOnlyList<TimesheetMonthAvailabilityDto>> ListMonthsWithEntriesAsync(
        Guid? projectId = null,
        string? clientName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetEntry>> ListOpenByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all open timesheet entries (any project), tracked for updates.
    /// </summary>
    Task<IReadOnlyList<TimesheetEntry>> ListOpenAsync(CancellationToken cancellationToken = default);

    Task<TimesheetEntry?> GetLatestOpenByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(TimesheetEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(TimesheetEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAsync(TimesheetEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for prompt activity events.
/// </summary>
public interface IActivityEventRepository
{
    Task<PromptActivityEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PromptActivityEvent?> FindByExternalIdAsync(
        string externalEventId,
        EditorType editor,
        CancellationToken cancellationToken = default);

    Task AddAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default);

    Task UpdateAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromptActivityEvent>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        bool? unallocatedOnly = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest activity event by timestamp, if any.
    /// </summary>
    Task<PromptActivityEvent?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts activity events matching browse filters (SQL).
    /// </summary>
    Task<int> CountAsync(
        ActivityEventPageFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a page of activity events with SQL OFFSET/LIMIT.
    /// </summary>
    Task<IReadOnlyList<PromptActivityEvent>> ListPagedAsync(
        ActivityEventPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct model / branch / event-type / day facets for a project range.
    /// </summary>
    Task<PromptFacetsDto> GetPromptFacetsAsync(
        Guid projectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromptActivityEvent>> ListBySessionAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest <see cref="ActivityEventType.PromptSubmitted"/> timestamp for a session, if any.
    /// </summary>
    Task<DateTimeOffset?> GetLatestPromptTimestampAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest project prompt in <c>[fromUtc, toUtc)</c>, if any.
    /// </summary>
    Task<DateTimeOffset?> GetLatestPromptTimestampForProjectAsync(
        Guid projectId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default);

    Task<PromptActivityEvent?> FindByExternalRequestIdAsync(
        string externalRequestId,
        CancellationToken cancellationToken = default);

    Task<PromptActivityEvent?> FindByExternalConversationIdAsync(
        string externalConversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the prompt (with a project) whose timestamp (rounded to the second)
    /// is closest at or before <paramref name="timestampUtc"/> and whose model
    /// matches <paramref name="model"/> (case-insensitive; empty matches empty).
    /// Already-attributed prompts remain eligible — multiple usage rows may
    /// link to the same prompt (many usages → one prompt).
    /// </summary>
    Task<PromptActivityEvent?> FindClosestPriorPromptWithProjectAsync(
        DateTimeOffset timestampUtc,
        string? model,
        CancellationToken cancellationToken = default);

    Task<int> CountUnallocatedAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task AssignProjectAsync(
        IReadOnlyList<Guid> eventIds,
        Guid projectId,
        AttributionMethod method,
        AttributionConfidence confidence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes unallocated activity events by id (ignores allocated rows).
    /// Returns the number of events removed.
    /// </summary>
    Task<int> DeleteUnallocatedByIdsAsync(
        IReadOnlyList<Guid> eventIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for calculated activity windows.
/// </summary>
public interface IActivityWindowRepository
{
    Task AddRangeAsync(IEnumerable<ActivityWindow> windows, CancellationToken cancellationToken = default);

    Task DeleteForScopeAsync(
        Guid? projectId,
        Guid? editorSessionId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityWindow>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<long> SumDurationSecondsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for imported external usage records.
/// </summary>
public interface IExternalUsageRepository
{
    Task<ExternalUsageRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExternalUsageRecord?> FindByExternalRecordIdAsync(
        UsageSource source,
        string externalRecordId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ExternalUsageRecord record, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<ExternalUsageRecord> records, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalUsageRecord>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        UsageSource? source = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalUsageRecord>> ListUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts usage rows in range that have no project attribution.
    /// </summary>
    Task<int> CountUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes unallocated usage rows (and their attribution placeholders) in the range.
    /// Returns the number of usage records removed.
    /// </summary>
    Task<int> DeleteUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for usage attributions.
/// </summary>
public interface IUsageAttributionRepository
{
    Task AddAsync(UsageAttribution attribution, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<UsageAttribution> attributions, CancellationToken cancellationToken = default);

    Task DeleteForUsageRecordAsync(Guid externalUsageRecordId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageAttribution>> ListByUsageRecordAsync(
        Guid externalUsageRecordId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageAttribution>> ListByUsageRecordIdsAsync(
        IReadOnlyCollection<Guid> externalUsageRecordIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageAttribution>> ListByActivityEventIdsAsync(
        IReadOnlyCollection<Guid> activityEventIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageAttribution>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasAttributionAsync(Guid externalUsageRecordId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for import batches.
/// </summary>
public interface IImportBatchRepository
{
    Task<ImportBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ImportBatch?> FindByFileHashAsync(string fileHash, CancellationToken cancellationToken = default);

    Task AddAsync(ImportBatch batch, CancellationToken cancellationToken = default);

    Task UpdateAsync(ImportBatch batch, CancellationToken cancellationToken = default);

    Task<ImportBatch?> GetLatestAsync(UsageSource? source = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for cost allocation rules.
/// </summary>
public interface ICostAllocationRuleRepository
{
    Task<IReadOnlyList<CostAllocationRule>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task<CostAllocationRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(CostAllocationRule rule, CancellationToken cancellationToken = default);

    Task UpdateAsync(CostAllocationRule rule, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for tracking API keys (hashes only).
/// </summary>
public interface IApiKeyRepository
{
    Task<TrackingApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TrackingApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackingApiKey>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task AddAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default);

    Task UpdateAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unit of work for committing repository changes.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
