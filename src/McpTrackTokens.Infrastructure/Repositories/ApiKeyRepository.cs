using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IApiKeyRepository"/>.
/// </summary>
public sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly TrackingDbContext _db;

    public ApiKeyRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<TrackingApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.TrackingApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<TrackingApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken = default)
        => _db.TrackingApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrackingApiKey>> ListAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _db.TrackingApiKeys.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(k => k.IsActive);
        }

        return await SqliteDateTimeQuery.MaterializeAsync(
            query,
            orderBy: items => items.OrderByDescending(k => k.CreatedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default)
        => await _db.TrackingApiKeys.AddAsync(apiKey, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default)
    {
        var entry = _db.Entry(apiKey);
        if (entry.State == EntityState.Detached)
        {
            _db.TrackingApiKeys.Update(apiKey);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(TrackingApiKey apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        _db.TrackingApiKeys.Remove(apiKey);
        return Task.CompletedTask;
    }
}
