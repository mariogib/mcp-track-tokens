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
    public async Task<IReadOnlyList<UsageAttribution>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        if (!SqliteDateTimeQuery.IsSqlite(_db))
        {
            var query = _db.UsageAttributions.AsNoTracking().AsQueryable();
            if (projectId is Guid pid)
            {
                query = query.Where(a => a.ProjectId == pid);
            }

            return await query
                .Where(a => a.CreatedAtUtc >= from && a.CreatedAtUtc <= to)
                .OrderByDescending(a => a.CreatedAtUtc)
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

        SqliteDateTimePaging.AppendUnixRange(where, args, "CreatedAtUtc", from, to);
        var sql = "SELECT * FROM UsageAttributions " + where + " ORDER BY CreatedAtUtc DESC";
        return await SqliteDateTimeQuery
            .FromSqlAsync(_db.UsageAttributions, sql, args, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> HasAttributionAsync(Guid externalUsageRecordId, CancellationToken cancellationToken = default)
        => _db.UsageAttributions.AsNoTracking()
            .AnyAsync(a => a.ExternalUsageRecordId == externalUsageRecordId, cancellationToken);
}
