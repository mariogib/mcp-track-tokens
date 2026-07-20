using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITimesheetCategoryRepository"/>.
/// </summary>
public sealed class TimesheetCategoryRepository : ITimesheetCategoryRepository
{
    private readonly TrackingDbContext _db;

    public TimesheetCategoryRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<TimesheetCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.TimesheetCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<TimesheetCategory?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        var all = await _db.TimesheetCategories.ToListAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(c =>
            string.Equals(c.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetCategory>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.TimesheetCategories.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        var all = await _db.TimesheetCategories.AsNoTracking().ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return all.Any(c =>
            string.Equals(c.Name, normalized, StringComparison.OrdinalIgnoreCase) &&
            (excludingId is null || c.Id != excludingId));
    }

    /// <inheritdoc />
    public async Task AddAsync(TimesheetCategory category, CancellationToken cancellationToken = default)
        => await _db.TimesheetCategories.AddAsync(category, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateAsync(TimesheetCategory category, CancellationToken cancellationToken = default)
    {
        var tracked = _db.Entry(category);
        if (tracked.State == EntityState.Detached)
        {
            _db.TimesheetCategories.Update(category);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(TimesheetCategory category, CancellationToken cancellationToken = default)
    {
        _db.TimesheetCategories.Remove(category);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountEntriesAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => _db.TimesheetEntries.CountAsync(e => e.CategoryId == categoryId, cancellationToken);
}
