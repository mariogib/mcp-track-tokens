using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IImportBatchRepository"/>.
/// </summary>
public sealed class ImportBatchRepository : IImportBatchRepository
{
    private readonly TrackingDbContext _db;

    public ImportBatchRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<ImportBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ImportBatch?> FindByFileHashAsync(string fileHash, CancellationToken cancellationToken = default)
        => _db.ImportBatches
            .FirstOrDefaultAsync(b => b.FileHash == fileHash, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ImportBatch batch, CancellationToken cancellationToken = default)
        => await _db.ImportBatches.AddAsync(batch, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        _db.ImportBatches.Update(batch);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ImportBatch?> GetLatestAsync(UsageSource? source = null, CancellationToken cancellationToken = default)
    {
        var query = _db.ImportBatches.AsNoTracking().AsQueryable();
        if (source is not null)
        {
            query = query.Where(b => b.Source == source.Value);
        }

        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            query,
            orderBy: items => items.OrderByDescending(b => b.StartedAtUtc),
            take: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault();
    }
}
