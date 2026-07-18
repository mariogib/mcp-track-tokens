using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.ValueObjects;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISessionRepository"/>.
/// </summary>
public sealed class SessionRepository : ISessionRepository
{
    private readonly TrackingDbContext _db;

    public SessionRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<EditorSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.EditorSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<EditorSession?> GetByExternalSessionIdAsync(
        string externalSessionId,
        EditorType? editor = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.EditorSessions.AsNoTracking().Where(s => s.ExternalSessionId == externalSessionId);
        if (editor is not null)
        {
            query = query.Where(s => s.Editor == editor.Value);
        }

        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            query,
            orderBy: items => items.OrderByDescending(s => s.StartedAtUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.EditorSessions.AsNoTracking().Where(s => s.Status == SessionStatus.Active),
            orderBy: items => items.OrderByDescending(s => s.LastActivityAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<EditorSession?> GetActiveForWorkspaceAsync(
        EditorType editor,
        string? workspacePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspace = NormalizedPath.Normalize(workspacePath);
        var active = await SqliteDateTimeQuery.MaterializeAsync(
            _db.EditorSessions.AsNoTracking()
                .Where(s => s.Status == SessionStatus.Active && s.Editor == editor),
            orderBy: items => items.OrderByDescending(s => s.LastActivityAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return active.FirstOrDefault(s =>
            string.Equals(
                NormalizedPath.Normalize(s.WorkspacePath),
                normalizedWorkspace,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> GetActiveAtAsync(
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken = default)
    {
        var at = timestampUtc.ToUniversalTime();
        return await SqliteDateTimeQuery.MaterializeAsync(
            _db.EditorSessions.AsNoTracking(),
            s => s.StartedAtUtc <= at && (s.EndedAtUtc == null || s.EndedAtUtc >= at),
            items => items.OrderByDescending(s => s.LastActivityAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();
        return await SqliteDateTimeQuery.MaterializeAsync(
            _db.EditorSessions.AsNoTracking().Where(s => s.ProjectId == projectId),
            s => (from is null || s.StartedAtUtc >= from || (s.EndedAtUtc != null && s.EndedAtUtc >= from)) &&
                 (to is null || s.StartedAtUtc <= to),
            items => items.OrderByDescending(s => s.StartedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(EditorSession session, CancellationToken cancellationToken = default)
        => await _db.EditorSessions.AddAsync(session, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(EditorSession session, CancellationToken cancellationToken = default)
    {
        // Never call Update() on an already-tracked entity — especially Added.
        // Update() forces Modified and issues an UPDATE that fails for newly inserted rows
        // (DbUpdateConcurrencyException: expected 1 row, affected 0).
        var entry = _db.Entry(session);
        if (entry.State == EntityState.Detached)
        {
            _db.EditorSessions.Update(session);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task TouchActivityAsync(
        Guid sessionId,
        DateTimeOffset activityAtUtc,
        Guid? assignProjectId = null,
        CancellationToken cancellationToken = default)
    {
        var at = activityAtUtc.ToUniversalTime();
        var tracked = _db.ChangeTracker.Entries<EditorSession>()
            .FirstOrDefault(e => e.Entity.Id == sessionId);

        if (tracked is { State: EntityState.Added })
        {
            tracked.Entity.RecordActivity(at);
            if (assignProjectId is Guid projectId && tracked.Entity.ProjectId is null)
            {
                tracked.Entity.ProjectId = projectId;
            }

            return;
        }

        // Bypass RowVersion concurrency so parallel hook/queue posts do not 500.
        await _db.EditorSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.LastActivityAtUtc, at)
                    .SetProperty(s => s.UpdatedAtUtc, at),
                cancellationToken)
            .ConfigureAwait(false);

        if (assignProjectId is Guid pid)
        {
            await _db.EditorSessions
                .Where(s => s.Id == sessionId && s.ProjectId == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(s => s.ProjectId, pid),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
