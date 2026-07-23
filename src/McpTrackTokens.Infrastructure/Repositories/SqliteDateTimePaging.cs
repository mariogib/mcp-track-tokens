using System.Globalization;
using System.Text;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// Builds SQLite-translatable date filters and LIMIT/OFFSET paging for DateTimeOffset TEXT columns.
/// EF Core cannot translate DateTimeOffset comparisons or ORDER BY on SQLite.
/// </summary>
internal static class SqliteDateTimePaging
{
    /// <summary>
    /// Second-precision unixepoch expression over an EF-stored DateTimeOffset TEXT column.
    /// </summary>
    public static string UnixEpochExpr(string columnName, string? tableAlias = null)
    {
        var column = tableAlias is null
            ? $"\"{columnName}\""
            : $"{tableAlias}.\"{columnName}\"";
        return $"unixepoch(replace(substr({column}, 1, 19), ' ', 'T') || 'Z')";
    }

    public static long ToUnixSeconds(DateTimeOffset value)
        => value.ToUniversalTime().ToUnixTimeSeconds();

    public static void AppendUnixRange(
        StringBuilder where,
        List<object> args,
        string columnName,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool fromInclusive = true,
        bool toInclusive = true)
    {
        var expr = UnixEpochExpr(columnName);
        if (fromUtc is DateTimeOffset from)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND {expr} {(fromInclusive ? ">=" : ">")} {{{args.Count}}}");
            args.Add(ToUnixSeconds(from));
        }

        if (toUtc is DateTimeOffset to)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND {expr} {(toInclusive ? "<=" : "<")} {{{args.Count}}}");
            args.Add(ToUnixSeconds(to));
        }
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
