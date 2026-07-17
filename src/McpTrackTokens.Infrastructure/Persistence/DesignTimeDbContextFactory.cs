using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> migrations tooling.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    /// <inheritdoc />
    public TrackingDbContext CreateDbContext(string[] args)
    {
        var databasePath = TrackingOptions.ExpandPath("~/.mcp-track-tokens/mcp-track-tokens.db");
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<TrackingDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new TrackingDbContext(options);
    }
}
