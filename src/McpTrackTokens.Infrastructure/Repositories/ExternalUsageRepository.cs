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
            items => items.OrderByDescending(r => r.TimestampUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

        // Allocated = has at least one attribution row with a project id.
        // Unallocated placeholder rows (method Unallocated, ProjectId null) do not count.
        var allocatedIds = await _db.UsageAttributions.AsNoTracking()
            .Where(a => a.ProjectId != null)
            .Select(a => a.ExternalUsageRecordId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var allocated = allocatedIds.ToHashSet();

        return await SqliteDateTimeQuery.MaterializeAsync(
            _db.ExternalUsageRecords.AsNoTracking(),
            r => r.TimestampUtc >= from && r.TimestampUtc <= to && !allocated.Contains(r.Id),
            items => items.OrderByDescending(r => r.TimestampUtc),
            take: limit,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
