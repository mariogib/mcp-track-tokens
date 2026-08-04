using System.Globalization;
using System.Text;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// Builds SQLite-translatable date filters and LIMIT/OFFSET paging for DateTimeOffset TEXT columns.
/// EF Core cannot translate DateTimeOffset comparisons or ORDER BY on SQLite.
/// </summary>
/// <remarks>
/// Range filters compare the stored TEXT column to second-precision UTC bounds
/// (<c>yyyy-MM-dd HH:mm:ss</c>) so B-tree indexes on those columns can be used.
/// Semantics match the previous <c>unixepoch(substr(..., 1, 19))</c> second-precision filters.
/// </remarks>
internal static class SqliteDateTimePaging
{
    /// <summary>
    /// Quoted column reference, optionally qualified with a table alias.
    /// </summary>
    public static string ColumnRef(string columnName, string? tableAlias = null)
        => tableAlias is null
            ? $"\"{columnName}\""
            : $"{tableAlias}.\"{columnName}\"";

    /// <summary>
    /// Formats a UTC second-precision bound for lexicographic compare against EF SQLite TEXT.
    /// </summary>
    public static string FormatSecondBound(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Inclusive lower / inclusive upper range on a DateTimeOffset TEXT column (second precision).
    /// </summary>
    public static void AppendTextRange(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool fromInclusive = true,
        bool toInclusive = true,
        string? tableAlias = null)
    {
        var column = ColumnRef(columnName, tableAlias);
        if (fromUtc is DateTimeOffset from)
        {
            var bound = fromInclusive
                ? FormatSecondBound(from)
                : FormatSecondBound(from.ToUniversalTime().AddSeconds(1));
            where.Append(CultureInfo.InvariantCulture, $" AND {column} >= {{{args.Count}}}");
            args.Add(bound);
        }

        if (toUtc is DateTimeOffset to)
        {
            // Inclusive upper at second N ⇒ column &lt; second N+1 (covers fractional seconds in N).
            var bound = toInclusive
                ? FormatSecondBound(to.ToUniversalTime().AddSeconds(1))
                : FormatSecondBound(to);
            where.Append(CultureInfo.InvariantCulture, $" AND {column} < {{{args.Count}}}");
            args.Add(bound);
        }
    }

    /// <summary>
    /// Appends <c>column &gt;= bound</c> using second-precision flooring (index-friendly).
    /// </summary>
    public static void AppendGreaterThanOrEqual(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset value,
        string? tableAlias = null)
    {
        where.Append(CultureInfo.InvariantCulture,
            $" AND {ColumnRef(columnName, tableAlias)} >= {{{args.Count}}}");
        args.Add(FormatSecondBound(value));
    }

    /// <summary>
    /// Appends <c>column &lt;= value</c> at second precision via <c>column &lt; value+1s</c>.
    /// </summary>
    public static void AppendLessThanOrEqual(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset value,
        string? tableAlias = null)
    {
        where.Append(CultureInfo.InvariantCulture,
            $" AND {ColumnRef(columnName, tableAlias)} < {{{args.Count}}}");
        args.Add(FormatSecondBound(value.ToUniversalTime().AddSeconds(1)));
    }

    /// <summary>
    /// Appends <c>column &lt; value</c> at second precision (exclusive upper / strict less-than).
    /// </summary>
    public static void AppendLessThan(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset value,
        string? tableAlias = null)
    {
        where.Append(CultureInfo.InvariantCulture,
            $" AND {ColumnRef(columnName, tableAlias)} < {{{args.Count}}}");
        args.Add(FormatSecondBound(value));
    }

    /// <summary>
    /// Appends <c>column &gt; value</c> at second precision via <c>column &gt;= value+1s</c>.
    /// </summary>
    public static void AppendGreaterThan(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset value,
        string? tableAlias = null)
    {
        where.Append(CultureInfo.InvariantCulture,
            $" AND {ColumnRef(columnName, tableAlias)} >= {{{args.Count}}}");
        args.Add(FormatSecondBound(value.ToUniversalTime().AddSeconds(1)));
    }

    public static void AppendLikeContains(
        StringBuilder where,
        List<object> args,
        string columnExpression,
        string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return;
        }

        where.Append(CultureInfo.InvariantCulture, $" AND ({columnExpression}) LIKE {{{args.Count}}} ESCAPE '\\'");
        args.Add("%" + EscapeLike(term) + "%");
    }

    public static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public static (int Skip, int Take) NormalizePage(int pageIndex, int pageSize, int maxPageSize = 100)
    {
        var size = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, maxPageSize);
        var index = Math.Max(0, pageIndex);
        return (index * size, size);
    }
}
