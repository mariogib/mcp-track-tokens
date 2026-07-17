using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core unit of work that commits tracked changes.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly TrackingDbContext _db;

    public UnitOfWork(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
