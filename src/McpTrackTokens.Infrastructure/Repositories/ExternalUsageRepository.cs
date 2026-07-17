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
        var query = _db.ExternalUsageRecords.AsNoTracking().AsQueryable();
        if (source is not null)
        {
            query = query.Where(r => r.Source == source.Value);
        }

        return await SqliteDateTimeQuery.MaterializeAsync(
            query,
            r => r.TimestampUtc >= from && r.TimestampUtc <= to,
            items => items.OrderBy(r => r.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalUsageRecord>> ListUnallocatedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var attributedIds = await _db.UsageAttributions.AsNoTracking()
            .Select(a => a.ExternalUsageRecordId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var attributed = attributedIds.ToHashSet();

        return await SqliteDateTimeQuery.MaterializeAsync(
            _db.ExternalUsageRecords.AsNoTracking(),
            r => r.TimestampUtc >= from && r.TimestampUtc <= to && !attributed.Contains(r.Id),
            items => items.OrderBy(r => r.TimestampUtc),
            take: limit,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
