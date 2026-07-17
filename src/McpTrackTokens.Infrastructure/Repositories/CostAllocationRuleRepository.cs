using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICostAllocationRuleRepository"/>.
/// </summary>
public sealed class CostAllocationRuleRepository : ICostAllocationRuleRepository
{
    private readonly TrackingDbContext _db;

    public CostAllocationRuleRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CostAllocationRule>> ListEnabledAsync(CancellationToken cancellationToken = default)
        => await _db.CostAllocationRules.AsNoTracking()
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<CostAllocationRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.CostAllocationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(CostAllocationRule rule, CancellationToken cancellationToken = default)
        => await _db.CostAllocationRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(CostAllocationRule rule, CancellationToken cancellationToken = default)
    {
        _db.CostAllocationRules.Update(rule);
        return Task.CompletedTask;
    }
}
