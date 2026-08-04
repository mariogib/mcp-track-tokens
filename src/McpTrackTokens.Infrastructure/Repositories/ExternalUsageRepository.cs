using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IExternalUsageRepository"/>.
/// </summary>
public sealed class ExternalUsageRepository : IExternalUsageRepository
{
    private readonly TrackingDbContext _db;

    public ExternalUsageRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<ExternalUsageRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.ExternalUsageRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ExternalUsageRecord?> FindByExternalRecordIdAsync(
        UsageSource source,
        string externalRecordId,
        CancellationToken cancellationToken = default)
        => _db.ExternalUsageRecords.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Source == source && r.ExternalRecordId == externalRecordId,
                cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ExternalUsageRecord record, CancellationToken cancellationToken = default)
        => await _db.ExternalUsageRecords.AddAsync(record, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<ExternalUsageRecord> records, CancellationToken cancellationToken = default)
        => await _db.ExternalUsageRecords.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalUsageRecord>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        UsageSource? source = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.ExternalUsageRecords.AsNoTracking().AsQueryable();
            if (source is not null)
            {
                query = query.Where(r => r.Source == source.Value);
            }

            return await query
                .Where(r => r.TimestampUtc >= from && r.TimestampUtc <= to)
                .OrderByDescending(r => r.TimestampUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();
        if (source is UsageSource usageSource)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND Source = {{{args.Count}}}");
            args.Add(usageSource.ToString());
        }

        SqliteDateTimePaging.AppendTextRange(where, args, "TimestampUtc", from, to);
        var sql = "SELECT * FROM ExternalUsageRecords " + where + " ORDER BY TimestampUtc DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.ExternalUsageRecords, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A usage row is unallocated when it has no attribution with a project.
    /// Rows that only have <see cref="AttributionMethod.Unallocated"/> placeholders
    /// (written by reconciliation when no prompt match exists) remain unallocated.
    /// </remarks>
    public async Task<IReadOnlyList<ExternalUsageRecord>> ListUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var allocatedIds = _db.UsageAttributions.AsNoTracking()
                .Where(a => a.ProjectId != null)
                .Select(a => a.ExternalUsageRecordId);

            var query = _db.ExternalUsageRecords.AsNoTracking()
                .Where(r =>
                    r.TimestampUtc >= from &&
                    r.TimestampUtc <= to &&
                    !allocatedIds.Contains(r.Id))
                .OrderByDescending(r => r.TimestampUtc);

            if (limit is int take and > 0)
            {
                return await query.Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var where = new StringBuilder(
            "WHERE NOT EXISTS (SELECT 1 FROM UsageAttributions a WHERE a.ExternalUsageRecordId = ExternalUsageRecords.Id AND a.ProjectId IS NOT NULL)");
        var args = new List<object>();
        SqliteDateTimePaging.AppendTextRange(where, args, "TimestampUtc", from, to);
        var sql = new StringBuilder("SELECT * FROM ExternalUsageRecords ")
            .Append(where)
            .Append(" ORDER BY TimestampUtc DESC");
        if (limit is int lim and > 0)
        {
            sql.Append(CultureInfo.InvariantCulture, $" LIMIT {{{args.Count}}}");
            args.Add(lim);
        }

        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.ExternalUsageRecords, sql.ToString(), args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var allocatedIds = _db.UsageAttributions.AsNoTracking()
                .Where(a => a.ProjectId != null)
                .Select(a => a.ExternalUsageRecordId);

            return await _db.ExternalUsageRecords.AsNoTracking()
                .Where(r =>
                    r.TimestampUtc >= from &&
                    r.TimestampUtc <= to &&
                    !allocatedIds.Contains(r.Id))
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var where = new StringBuilder(
            "WHERE NOT EXISTS (SELECT 1 FROM UsageAttributions a WHERE a.ExternalUsageRecordId = ExternalUsageRecords.Id AND a.ProjectId IS NOT NULL)");
        var args = new List<object>();
        SqliteDateTimePaging.AppendTextRange(where, args, "TimestampUtc", from, to);
        var sql = "SELECT COUNT(*) AS \"Value\" FROM ExternalUsageRecords " + where;
        return await _db.Database
            .SqlQueryRaw<int>(sql, args.ToArray())
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var records = await ListUnallocatedAsync(fromUtc, toUtc, limit: null, cancellationToken)
            .ConfigureAwait(false);
        if (records.Count == 0)
        {
            return 0;
        }

        var ids = records.Select(r => r.Id).ToHashSet();
        await _db.UsageAttributions
            .Where(a => ids.Contains(a.ExternalUsageRecordId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _db.ExternalUsageRecords
            .Where(r => ids.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
