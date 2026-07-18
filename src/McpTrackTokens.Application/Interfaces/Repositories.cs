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

    Task<IReadOnlyList<EditorSession>> GetActiveAtAsync(
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditorSession>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(EditorSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(EditorSession session, CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<PromptActivityEvent>> ListBySessionAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default);

    Task<PromptActivityEvent?> FindByExternalRequestIdAsync(
        string externalRequestId,
        CancellationToken cancellationToken = default);

    Task<PromptActivityEvent?> FindByExternalConversationIdAsync(
        string externalConversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the prompt (with a project) whose timestamp (rounded to the second)
    /// is closest at or before <paramref name="timestampUtc"/>.
    /// Already-attributed prompts remain eligible — multiple usage rows may
    /// link to the same prompt (many usages → one prompt).
    /// </summary>
    Task<PromptActivityEvent?> FindClosestPriorPromptWithProjectAsync(
        DateTimeOffset timestampUtc,
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
}

/// <summary>
/// Unit of work for committing repository changes.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
