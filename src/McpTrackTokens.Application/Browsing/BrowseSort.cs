namespace McpTrackTokens.Application.Browsing;

/// <summary>
/// Shared helpers for allowlisted column sorting on OFFSET/LIMIT browse queries.
/// </summary>
public static class BrowseSort
{
    /// <summary>
    /// Builds a stable ORDER BY clause from an allowlisted column map.
    /// Unknown columns fall back to <paramref name="defaultOrderBy"/>.
    /// </summary>
    public static string ResolveOrderBy(
        string? sortBy,
        string? sortDirection,
        IReadOnlyDictionary<string, string> columns,
        string defaultOrderBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultOrderBy);
        ArgumentNullException.ThrowIfNull(columns);

        var key = sortBy?.Trim();
        if (string.IsNullOrEmpty(key) || !columns.TryGetValue(key, out var expression))
        {
            return defaultOrderBy;
        }

        var descending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        return $"{expression} {(descending ? "DESC" : "ASC")}";
    }
}
