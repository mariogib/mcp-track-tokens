using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.DTOs;
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

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            return await _db.EditorSessions.AsNoTracking()
                .Where(s => s.StartedAtUtc <= at && (s.EndedAtUtc == null || s.EndedAtUtc >= at))
                .OrderByDescending(s => s.LastActivityAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
        var ended = SqliteDateTimePaging.UnixEpochExpr("EndedAtUtc");
        var atSec = SqliteDateTimePaging.ToUnixSeconds(at);
        var sql =
            $"SELECT * FROM EditorSessions WHERE {started} <= {{0}} AND (EndedAtUtc IS NULL OR {ended} >= {{0}}) ORDER BY LastActivityAtUtc DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.EditorSessions, sql, [atSec], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => await ListAsync(projectId, fromUtc, toUtc, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.EditorSessions.AsNoTracking().AsQueryable();
            if (projectId is Guid pid)
            {
                query = query.Where(s => s.ProjectId == pid);
            }

            if (from is DateTimeOffset fromValue)
            {
                query = query.Where(s =>
                    s.StartedAtUtc >= fromValue || (s.EndedAtUtc != null && s.EndedAtUtc >= fromValue));
            }

            if (to is DateTimeOffset toValue)
            {
                query = query.Where(s => s.StartedAtUtc <= toValue);
            }

            return await query
                .OrderByDescending(s => s.StartedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();
        if (projectId is Guid project)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND ProjectId = {{{args.Count}}}");
            args.Add(project);
        }

        if (from is DateTimeOffset fromBound)
        {
            var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
            var ended = SqliteDateTimePaging.UnixEpochExpr("EndedAtUtc");
            where.Append(CultureInfo.InvariantCulture,
                $" AND ({started} >= {{{args.Count}}} OR (EndedAtUtc IS NOT NULL AND {ended} >= {{{args.Count}}}))");
            args.Add(SqliteDateTimePaging.ToUnixSeconds(fromBound));
        }

        if (to is DateTimeOffset toBound)
        {
            var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
            where.Append(CultureInfo.InvariantCulture, $" AND {started} <= {{{args.Count}}}");
            args.Add(SqliteDateTimePaging.ToUnixSeconds(toBound));
        }

        var sql = "SELECT * FROM EditorSessions " + where + " ORDER BY StartedAtUtc DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.EditorSessions, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        SessionPageFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (where, args) = BuildBrowseWhere(filter);
        var sql = "SELECT COUNT(*) AS \"Value\" FROM EditorSessions " + where;
        return await _db.Database
            .SqlQueryRaw<int>(sql, args.ToArray())
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EditorSession>> ListPagedAsync(
        SessionPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (skip, take) = SqliteDateTimePaging.NormalizePage(pageIndex, pageSize);
        var (where, args) = BuildBrowseWhere(filter);
        var sql = new StringBuilder()
            .Append("SELECT * FROM EditorSessions ")
            .Append(where)
            .Append(CultureInfo.InvariantCulture,
                $" ORDER BY StartedAtUtc DESC, Id DESC LIMIT {{{args.Count}}} OFFSET {{{args.Count + 1}}}");
        args.Add(take);
        args.Add(skip);

        return await _db.EditorSessions
            .FromSqlRaw(sql.ToString(), args.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static (string WhereSql, List<object> Args) BuildBrowseWhere(SessionPageFilter filter)
    {
        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();

        if (filter.ProjectId is Guid projectId)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND ProjectId = {{{args.Count}}}");
            args.Add(projectId);
        }

        if (filter.FromUtc is DateTimeOffset fromBound)
        {
            var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
            var ended = SqliteDateTimePaging.UnixEpochExpr("EndedAtUtc");
            where.Append(CultureInfo.InvariantCulture,
                $" AND ({started} >= {{{args.Count}}} OR (EndedAtUtc IS NOT NULL AND {ended} >= {{{args.Count}}}))");
            args.Add(SqliteDateTimePaging.ToUnixSeconds(fromBound));
        }

        if (filter.ToUtc is DateTimeOffset toBound)
        {
            var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
            where.Append(CultureInfo.InvariantCulture, $" AND {started} <= {{{args.Count}}}");
            args.Add(SqliteDateTimePaging.ToUnixSeconds(toBound));
        }

        var status = filter.Status?.Trim();
        if (!string.IsNullOrEmpty(status))
        {
            if (string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase))
            {
                where.Append(" AND Status IN ('Ended', 'Abandoned')");
            }
            else
            {
                where.Append(CultureInfo.InvariantCulture, $" AND Status = {{{args.Count}}}");
                args.Add(status);
            }
        }

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = "%" + SqliteDateTimePaging.EscapeLike(search) + "%";
            where.Append(CultureInfo.InvariantCulture,
                $" AND (CAST(Id AS TEXT) LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(Editor,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(Branch,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(WorkspacePath,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(RepositoryPath,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(RemoteUrl,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(ExternalSessionId,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(MachineName,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(UserName,'') LIKE {{{args.Count}}} ESCAPE '\\'" +
                $" OR IFNULL(Status,'') LIKE {{{args.Count}}} ESCAPE '\\')");
            args.Add(pattern);
        }

        return (where.ToString(), args);
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
    public Task DeleteAsync(EditorSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var entry = _db.Entry(session);
        if (entry.State == EntityState.Detached)
        {
            _db.EditorSessions.Attach(session);
        }

        _db.EditorSessions.Remove(session);
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
