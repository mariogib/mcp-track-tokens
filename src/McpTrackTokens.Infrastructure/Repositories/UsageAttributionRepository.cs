using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUsageAttributionRepository"/>.
/// </summary>
public sealed class UsageAttributionRepository : IUsageAttributionRepository
{
    private readonly TrackingDbContext _db;

    public UsageAttributionRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AddAsync(UsageAttribution attribution, CancellationToken cancellationToken = default)
        => await _db.UsageAttributions.AddAsync(attribution, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddRangeAsync(
        IEnumerable<UsageAttribution> attributions,
        CancellationToken cancellationToken = default)
        => await _db.UsageAttributions.AddRangeAsync(attributions, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DeleteForUsageRecordAsync(Guid externalUsageRecordId, CancellationToken cancellationToken = default)
        => await _db.UsageAttributions
            .Where(a => a.ExternalUsageRecordId == externalUsageRecordId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> ListByUsageRecordAsync(
        Guid externalUsageRecordId,
        CancellationToken cancellationToken = default)
        => await SqliteDateTimeQuery.MaterializeAsync(
            _db.UsageAttributions.AsNoTracking().Where(a => a.ExternalUsageRecordId == externalUsageRecordId),
            orderBy: items => items.OrderByDescending(a => a.CreatedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> ListByUsageRecordIdsAsync(
        IReadOnlyCollection<Guid> externalUsageRecordIds,
        CancellationToken cancellationToken = default)
    {
        if (externalUsageRecordIds.Count == 0)
        {
            return [];
        }

        var ids = externalUsageRecordIds as HashSet<Guid> ?? externalUsageRecordIds.ToHashSet();
        // SQLite cannot ORDER BY DateTimeOffset in SQL — sort in memory.
        var items = await _db.UsageAttributions.AsNoTracking()
            .Where(a => ids.Contains(a.ExternalUsageRecordId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return items.OrderByDescending(a => a.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> ListByActivityEventIdsAsync(
        IReadOnlyCollection<Guid> activityEventIds,
        CancellationToken cancellationToken = default)
    {
        if (activityEventIds.Count == 0)
        {
            return [];
        }

        var ids = activityEventIds as HashSet<Guid> ?? activityEventIds.ToHashSet();
        var items = await _db.UsageAttributions.AsNoTracking()
            .Where(a => a.ActivityEventId != null && ids.Contains(a.ActivityEventId.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return items;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Period filters use the linked usage record's <c>TimestampUtc</c>
    /// (when the usage occurred), not the attribution row's <c>CreatedAtUtc</c>
    /// (when the attribution was written during import/reconcile).
    /// </remarks>
    public async Task<IReadOnlyList<UsageAttribution>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var fromBound = fromUtc.ToUniversalTime();
        var toBound = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.UsageAttributions.AsNoTracking()
                .Join(
                    _db.ExternalUsageRecords.AsNoTracking(),
                    attribution => attribution.ExternalUsageRecordId,
                    usage => usage.Id,
                    (attribution, usage) => new { Attribution = attribution, usage.TimestampUtc })
                .Where(row => row.TimestampUtc >= fromBound && row.TimestampUtc <= toBound);

            if (projectId is Guid pid)
            {
                query = query.Where(row => row.Attribution.ProjectId == pid);
            }

            return await query
                .OrderByDescending(row => row.TimestampUtc)
                .Select(row => row.Attribution)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var where = new StringBuilder("WHERE 1=1");
        var args = new List<object>();
        if (projectId is Guid project)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND a.\"ProjectId\" = {{{args.Count}}}");
            args.Add(project);
        }

        SqliteDateTimePaging.AppendTextRange(
            where,
            args,
            "TimestampUtc",
            fromBound,
            toBound,
            tableAlias: "u");
        var sql =
            "SELECT a.* FROM \"UsageAttributions\" AS a " +
            "INNER JOIN \"ExternalUsageRecords\" AS u ON a.\"ExternalUsageRecordId\" = u.\"Id\" " +
            where +
            " ORDER BY u.\"TimestampUtc\" DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.UsageAttributions, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> HasAttributionAsync(Guid externalUsageRecordId, CancellationToken cancellationToken = default)
        => _db.UsageAttributions.AsNoTracking()
            .AnyAsync(a => a.ExternalUsageRecordId == externalUsageRecordId, cancellationToken);
}
