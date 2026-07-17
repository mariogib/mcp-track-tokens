using Microsoft.EntityFrameworkCore;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// Helpers for SQLite's limited DateTimeOffset LINQ translation support.
/// </summary>
internal static class SqliteDateTimeQuery
{
    /// <summary>
    /// Materializes the query then applies an in-memory predicate and optional ordering.
    /// Used when SQLite cannot translate DateTimeOffset comparisons or ORDER BY clauses.
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
}
