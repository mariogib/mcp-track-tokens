using Microsoft.EntityFrameworkCore;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// Helpers for SQLite's limited DateTimeOffset LINQ translation support.
/// Prefer SQL unixepoch filters (<see cref="SqliteDateTimePaging"/>) for date ranges;
/// use <see cref="MaterializeAsync{T}"/> only for small, already-scoped sets that need
/// in-memory DateTimeOffset ordering.
/// </summary>
internal static class SqliteDateTimeQuery
{
    public static bool IsSqlite(DbContext db)
        => db.Database.IsSqlite();

    /// <summary>
    /// Materializes the query then applies an in-memory predicate and optional ordering.
    /// Used when SQLite cannot translate DateTimeOffset comparisons or ORDER BY clauses.
    /// Avoid for unbounded tables — push date filters into SQL with unixepoch instead.
    /// </summary>
    public static async Task<IReadOnlyList<T>> MaterializeAsync<T>(
        IQueryable<T> query,
        Func<T, bool>? predicate = null,
        Func<IEnumerable<T>, IOrderedEnumerable<T>>? orderBy = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var items = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<T> result = items;
        if (predicate is not null)
        {
            result = result.Where(predicate);
        }

        if (orderBy is not null)
        {
            result = orderBy(result);
        }

        if (take is int limit and > 0)
        {
            result = result.Take(limit);
        }

        return result.ToList();
    }

    /// <summary>
    /// Loads entities via raw SQL (SQLite unixepoch range queries) without a prior full-table materialize.
    /// </summary>
    public static async Task<IReadOnlyList<TEntity>> FromSqlAsync<TEntity>(
        DbSet<TEntity> set,
        string sql,
        IReadOnlyList<object> args,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => await set
            .FromSqlRaw(sql, args.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
