using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITimesheetEntryRepository"/>.
/// </summary>
public sealed class TimesheetEntryRepository : ITimesheetEntryRepository
{
    private readonly TrackingDbContext _db;

    public TimesheetEntryRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<TimesheetEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.TimesheetEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TimesheetEntry>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => ListAsync(projectId, fromUtc, toUtc, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntry>> ListAsync(
        Guid? projectId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.TimesheetEntries.AsNoTracking().AsQueryable();
            if (projectId is Guid pid)
            {
                query = query.Where(e => e.ProjectId == pid);
            }

            if (from is DateTimeOffset fromValue)
            {
                query = query.Where(e =>
                    e.StartedAtUtc >= fromValue || (e.EndedAtUtc != null && e.EndedAtUtc >= fromValue));
            }

            if (to is DateTimeOffset toValue)
            {
                query = query.Where(e => e.StartedAtUtc <= toValue);
            }

            return await query
                .OrderByDescending(e => e.StartedAtUtc)
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
            var started = SqliteDateTimePaging.ColumnRef("StartedAtUtc");
            var ended = SqliteDateTimePaging.ColumnRef("EndedAtUtc");
            var bound = SqliteDateTimePaging.FormatSecondBound(fromBound);
            where.Append(CultureInfo.InvariantCulture,
                $" AND ({started} >= {{{args.Count}}} OR (EndedAtUtc IS NOT NULL AND {ended} >= {{{args.Count}}}))");
            args.Add(bound);
        }

        if (to is DateTimeOffset toBound)
        {
            SqliteDateTimePaging.AppendLessThanOrEqual(where, args, "StartedAtUtc", toBound);
        }

        var sql = "SELECT * FROM TimesheetEntries " + where + " ORDER BY StartedAtUtc DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.TimesheetEntries, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        TimesheetEntryPageFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (fromSql, where, args) = BuildBrowseSql(filter, forCount: true);
        var sql = "SELECT COUNT(*) AS \"Value\" " + fromSql + " " + where;
        return await _db.Database
            .SqlQueryRaw<int>(sql, args.ToArray())
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntry>> ListPagedAsync(
        TimesheetEntryPageFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (skip, take) = SqliteDateTimePaging.NormalizePage(pageIndex, pageSize);
        var (fromSql, where, args) = BuildBrowseSql(filter, forCount: false);
        var sql = new StringBuilder()
            .Append("SELECT e.* ")
            .Append(fromSql)
            .Append(' ')
            .Append(where)
            .Append(CultureInfo.InvariantCulture,
                $" ORDER BY e.StartedAtUtc DESC, e.Id DESC LIMIT {{{args.Count}}} OFFSET {{{args.Count + 1}}}");
        args.Add(take);
        args.Add(skip);

        return await _db.TimesheetEntries
            .FromSqlRaw(sql.ToString(), args.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetMonthAvailabilityDto>> ListMonthsWithEntriesAsync(
        Guid? projectId = null,
        string? clientName = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedClient = clientName?.Trim();
        var needsJoin = !string.IsNullOrEmpty(normalizedClient);
        var fromSql = needsJoin
            ? "FROM TimesheetEntries AS e INNER JOIN Projects AS p ON p.Id = e.ProjectId"
            : "FROM TimesheetEntries AS e";

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();

        if (projectId is Guid pid)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND e.ProjectId = {{{args.Count}}}");
            args.Add(pid);
        }

        if (!string.IsNullOrEmpty(normalizedClient))
        {
            where.Append(CultureInfo.InvariantCulture,
                $" AND lower(IFNULL(p.ClientName,'')) = lower({{{args.Count}}})");
            args.Add(normalizedClient);
        }

        // StartedAtUtc is stored as DateTimeOffset TEXT; first 7 chars are YYYY-MM.
        var sql =
            "SELECT substr(e.StartedAtUtc, 1, 7) AS \"MonthKey\", COUNT(*) AS \"EntryCount\" " +
            fromSql + " " + where +
            " GROUP BY substr(e.StartedAtUtc, 1, 7) ORDER BY MonthKey DESC";

        var rows = await _db.Database
            .SqlQueryRaw<MonthCountRow>(sql, args.ToArray())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(ParseMonthRow)
            .Where(static m => m is not null)
            .Select(static m => m!)
            .ToList();
    }

    private static TimesheetMonthAvailabilityDto? ParseMonthRow(MonthCountRow row)
    {
        if (string.IsNullOrWhiteSpace(row.MonthKey) || row.MonthKey.Length < 7)
        {
            return null;
        }

        if (!int.TryParse(row.MonthKey.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(row.MonthKey.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            month is < 1 or > 12)
        {
            return null;
        }

        return new TimesheetMonthAvailabilityDto
        {
            Year = year,
            Month = month,
            EntryCount = row.EntryCount
        };
    }

    private sealed class MonthCountRow
    {
        public string MonthKey { get; set; } = string.Empty;

        public int EntryCount { get; set; }
    }

    private static (string FromSql, string WhereSql, List<object> Args) BuildBrowseSql(
        TimesheetEntryPageFilter filter,
        bool forCount)
    {
        var search = filter.Search?.Trim();
        var needsJoin = !string.IsNullOrEmpty(search);
        var fromSql = needsJoin
            ? "FROM TimesheetEntries AS e LEFT JOIN Projects AS p ON p.Id = e.ProjectId LEFT JOIN TimesheetCategories AS c ON c.Id = e.CategoryId"
            : "FROM TimesheetEntries AS e";

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();

        if (filter.ProjectId is Guid projectId)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND e.ProjectId = {{{args.Count}}}");
            args.Add(projectId);
        }

        // Match ListAsync range semantics with index-friendly TEXT bounds.
        if (filter.FromUtc is DateTimeOffset from)
        {
            var started = SqliteDateTimePaging.ColumnRef("StartedAtUtc", "e");
            var ended = SqliteDateTimePaging.ColumnRef("EndedAtUtc", "e");
            var bound = SqliteDateTimePaging.FormatSecondBound(from);
            where.Append(CultureInfo.InvariantCulture,
                $" AND ({started} >= {{{args.Count}}} OR (e.EndedAtUtc IS NOT NULL AND {ended} >= {{{args.Count}}}))");
            args.Add(bound);
        }

        if (filter.ToUtc is DateTimeOffset to)
        {
            SqliteDateTimePaging.AppendLessThanOrEqual(where, args, "StartedAtUtc", to, "e");
        }

        var openClosed = filter.OpenClosed?.Trim().ToLowerInvariant();
        if (openClosed == "open")
        {
            where.Append(" AND e.EndedAtUtc IS NULL");
        }
        else if (openClosed == "closed")
        {
            where.Append(" AND e.EndedAtUtc IS NOT NULL");
        }

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = "%" + SqliteDateTimePaging.EscapeLike(search) + "%";
            where.Append(CultureInfo.InvariantCulture,
                $" AND (IFNULL(e.Notes,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(p.Name,'') LIKE {{{args.Count}}} ESCAPE '\\' OR IFNULL(c.Name,'') LIKE {{{args.Count}}} ESCAPE '\\')");
            args.Add(pattern);
        }

        _ = forCount;
        return (fromSql, where.ToString(), args);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntry>> ListOpenByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.TimesheetEntries.Where(e => e.ProjectId == projectId && e.EndedAtUtc == null),
            orderBy: items => items.OrderBy(e => e.StartedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetEntry>> ListOpenAsync(
        CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.TimesheetEntries.Where(e => e.EndedAtUtc == null),
            orderBy: items => items.OrderBy(e => e.StartedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<TimesheetEntry?> GetLatestOpenByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var open = await ListOpenByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return open.OrderByDescending(e => e.StartedAtUtc).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task AddAsync(TimesheetEntry entry, CancellationToken cancellationToken = default)
        => await _db.TimesheetEntries.AddAsync(entry, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(TimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        var tracked = _db.Entry(entry);
        if (tracked.State == EntityState.Detached)
        {
            _db.TimesheetEntries.Update(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(TimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var tracked = _db.Entry(entry);
        if (tracked.State == EntityState.Detached)
        {
            _db.TimesheetEntries.Attach(entry);
        }

        _db.TimesheetEntries.Remove(entry);
        return Task.CompletedTask;
    }
}
