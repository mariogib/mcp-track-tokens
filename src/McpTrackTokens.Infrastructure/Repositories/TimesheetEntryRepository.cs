using Microsoft.EntityFrameworkCore;
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
    public async Task<IReadOnlyList<TimesheetEntry>> ListByProjectAsync(
        Guid projectId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();
        return await SqliteDateTimeQuery.MaterializeAsync(
            _db.TimesheetEntries.AsNoTracking().Where(e => e.ProjectId == projectId),
            e => (from is null || e.StartedAtUtc >= from || (e.EndedAtUtc != null && e.EndedAtUtc >= from)) &&
                 (to is null || e.StartedAtUtc <= to),
            items => items.OrderByDescending(e => e.StartedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
