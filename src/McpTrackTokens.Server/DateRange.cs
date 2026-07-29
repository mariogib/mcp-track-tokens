namespace McpTrackTokens.Server;

/// <summary>
/// Shared UTC date-range defaults for HTTP API and MCP tools.
/// </summary>
internal static class DateRange
{
    public static (DateTimeOffset From, DateTimeOffset To) Resolve(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var to = toUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var from = fromUtc?.ToUniversalTime() ?? to.AddDays(-30);
        if (from > to)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }
}
