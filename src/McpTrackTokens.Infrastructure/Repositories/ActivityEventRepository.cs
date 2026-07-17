using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IActivityEventRepository"/>.
/// </summary>
public sealed class ActivityEventRepository : IActivityEventRepository
{
    private readonly TrackingDbContext _db;

    public ActivityEventRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<PromptActivityEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PromptActivityEvent?> FindByExternalIdAsync(
        string externalEventId,
        EditorType editor,
        CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents
            .FirstOrDefaultAsync(
                e => e.ExternalEventId == externalEventId && e.Editor == editor,
                cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default)
        => await _db.PromptActivityEvents.AddAsync(activityEvent, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(PromptActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        _db.PromptActivityEvents.Update(activityEvent);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromptActivityEvent>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        bool? unallocatedOnly = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var query = _db.PromptActivityEvents.AsNoTracking().AsQueryable();

        if (projectId is Guid pid)
        {
            query = query.Where(e => e.ProjectId == pid);
        }

        if (unallocatedOnly == true)
        {
            query = query.Where(e =>
                e.ProjectId == null ||
                e.AttributionMethod == AttributionMethod.Unallocated);
        }

        return await SqliteDateTimeQuery.MaterializeAsync(
            query,
            e => e.TimestampUtc >= from && e.TimestampUtc <= to,
            items => items.OrderBy(e => e.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromptActivityEvent>> ListBySessionAsync(
        Guid editorSessionId,
        CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking().Where(e => e.EditorSessionId == editorSessionId),
            orderBy: items => items.OrderBy(e => e.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<PromptActivityEvent?> FindByExternalRequestIdAsync(
        string externalRequestId,
        CancellationToken cancellationToken = default)
        => _db.PromptActivityEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExternalRequestId == externalRequestId, cancellationToken);

    /// <inheritdoc />
    public async Task<PromptActivityEvent?> FindByExternalConversationIdAsync(
        string externalConversationId,
        CancellationToken cancellationToken = default)
    {
        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e => e.ExternalConversationId == externalConversationId),
            orderBy: items => items.OrderByDescending(e => e.TimestampUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<int> CountUnallocatedAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();
        var items = await SqliteDateTimeQuery.MaterializeAsync(
            _db.PromptActivityEvents.AsNoTracking()
                .Where(e => e.ProjectId == null || e.AttributionMethod == AttributionMethod.Unallocated),
            e => (from is null || e.TimestampUtc >= from) && (to is null || e.TimestampUtc <= to),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return items.Count;
    }

    /// <inheritdoc />
    public async Task AssignProjectAsync(
        IReadOnlyList<Guid> eventIds,
        Guid projectId,
        AttributionMethod method,
        AttributionConfidence confidence,
        CancellationToken cancellationToken = default)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var events = await _db.PromptActivityEvents
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var activityEvent in events)
        {
            activityEvent.ProjectId = projectId;
            activityEvent.AttributionMethod = method;
            activityEvent.AttributionConfidence = confidence;
        }
    }
}
