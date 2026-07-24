using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IActivityWindowRepository"/>.
/// </summary>
public sealed class ActivityWindowRepository : IActivityWindowRepository
{
    private readonly TrackingDbContext _db;

    public ActivityWindowRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<ActivityWindow> windows, CancellationToken cancellationToken = default)
        => await _db.ActivityWindows.AddRangeAsync(windows, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DeleteForScopeAsync(
        Guid? projectId,
        Guid? editorSessionId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.ActivityWindows.AsQueryable();
            if (projectId is Guid pid)
            {
                query = query.Where(w => w.ProjectId == pid);
            }

            if (editorSessionId is Guid sid)
            {
                query = query.Where(w => w.EditorSessionId == sid);
            }

            var matches = await query
                .Where(w => w.StartedAtUtc < to && w.EndedAtUtc > from)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (matches.Count == 0)
            {
                return;
            }

            _db.ActivityWindows.RemoveRange(matches);
            return;
        }

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();
        if (projectId is Guid project)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND ProjectId = {{{args.Count}}}");
            args.Add(project);
        }

        if (editorSessionId is Guid session)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND EditorSessionId = {{{args.Count}}}");
            args.Add(session);
        }

        AppendOverlapRange(where, args, from, to);
        var sql = "SELECT * FROM ActivityWindows " + where;
        var sqliteMatches = await _db.ActivityWindows
            .FromSqlRaw(sql, args.ToArray())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sqliteMatches.Count == 0)
        {
            return;
        }

        _db.ActivityWindows.RemoveRange(sqliteMatches);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityWindow>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.ActivityWindows.AsNoTracking().AsQueryable();
            if (projectId is Guid pid)
            {
                query = query.Where(w => w.ProjectId == pid);
            }

            return await query
                .Where(w => w.StartedAtUtc < to && w.EndedAtUtc > from)
                .OrderBy(w => w.StartedAtUtc)
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

        AppendOverlapRange(where, args, from, to);
        var sql = "SELECT * FROM ActivityWindows " + where + " ORDER BY StartedAtUtc";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.ActivityWindows, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> SumDurationSecondsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var windows = await ListAsync(fromUtc, toUtc, projectId, cancellationToken).ConfigureAwait(false);
        return windows.Sum(w => w.DurationSeconds);
    }

    private static void AppendOverlapRange(
        StringBuilder where,
        List<object> args,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        var started = SqliteDateTimePaging.UnixEpochExpr("StartedAtUtc");
        var ended = SqliteDateTimePaging.UnixEpochExpr("EndedAtUtc");
        where.Append(CultureInfo.InvariantCulture,
            $" AND {started} < {{{args.Count}}} AND {ended} > {{{args.Count + 1}}}");
        args.Add(SqliteDateTimePaging.ToUnixSeconds(toUtc));
        args.Add(SqliteDateTimePaging.ToUnixSeconds(fromUtc));
    }
}
