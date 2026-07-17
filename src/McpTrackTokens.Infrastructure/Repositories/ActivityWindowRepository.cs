using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IActivityWindowRepository"/>.
/// </summary>
public sealed class ActivityWindowRepository : IActivityWindowRepository
{
    private readonly TrackingDbContext _db;

    public ActivityWindowRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<ActivityWindow> windows, CancellationToken cancellationToken = default)
        => await _db.ActivityWindows.AddRangeAsync(windows, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DeleteForScopeAsync(
        Guid? projectId,
        Guid? editorSessionId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var query = _db.ActivityWindows.AsQueryable();

        if (projectId is Guid pid)
        {
            query = query.Where(w => w.ProjectId == pid);
        }

        if (editorSessionId is Guid sid)
        {
            query = query.Where(w => w.EditorSessionId == sid);
        }

        var matches = await SqliteDateTimeQuery.MaterializeAsync(
            query,
            w => w.StartedAtUtc < to && w.EndedAtUtc > from,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (matches.Count == 0)
        {
            return;
        }

        _db.ActivityWindows.RemoveRange(matches);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityWindow>> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var query = _db.ActivityWindows.AsNoTracking().AsQueryable();
        if (projectId is Guid pid)
        {
            query = query.Where(w => w.ProjectId == pid);
        }

        return await SqliteDateTimeQuery.MaterializeAsync(
            query,
            w => w.StartedAtUtc < to && w.EndedAtUtc > from,
            items => items.OrderBy(w => w.StartedAtUtc),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> SumDurationSecondsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var windows = await ListAsync(fromUtc, toUtc, projectId, cancellationToken).ConfigureAwait(false);
        return windows.Sum(w => w.DurationSeconds);
    }
}
