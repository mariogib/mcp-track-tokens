using McpTrackTokens.Application.Browsing;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class BrowseSortTests
{
    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Name",
            ["startedAtUtc"] = "StartedAtUtc",
        };

    [Fact]
    public void ResolveOrderBy_FallsBackToDefault_WhenSortMissing()
    {
        var order = BrowseSort.ResolveOrderBy(null, null, Columns, "StartedAtUtc DESC, Id DESC");
        Assert.Equal("StartedAtUtc DESC, Id DESC", order);
    }

    [Fact]
    public void ResolveOrderBy_FallsBackToDefault_WhenColumnUnknown()
    {
        var order = BrowseSort.ResolveOrderBy("nope", "asc", Columns, "Id ASC");
        Assert.Equal("Id ASC", order);
    }

    [Fact]
    public void ResolveOrderBy_UsesAllowlistedColumnAndDirection()
    {
        Assert.Equal(
            "Name ASC",
            BrowseSort.ResolveOrderBy("name", "asc", Columns, "Id DESC"));
        Assert.Equal(
            "StartedAtUtc DESC",
            BrowseSort.ResolveOrderBy("startedAtUtc", "DESC", Columns, "Id DESC"));
    }
}
