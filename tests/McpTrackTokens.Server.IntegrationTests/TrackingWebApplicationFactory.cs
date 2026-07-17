using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Server;
using McpTrackTokens.Server.Hosting;

namespace McpTrackTokens.Server.IntegrationTests;

/// <summary>
/// WebApplicationFactory backed by a temporary SQLite database.
/// </summary>
public sealed class TrackingWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "mcp-track-tokens-tests",
        $"{Guid.NewGuid():N}.db");

    private readonly string _exportPath = Path.Combine(
        Path.GetTempPath(),
        "mcp-track-tokens-tests",
        $"{Guid.NewGuid():N}-exports");

    public string ApiKey { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Directory.CreateDirectory(_exportPath);

        builder.UseEnvironment("Development");
        builder.UseSetting("Tracking:DatabaseProvider", "Sqlite");
        builder.UseSetting("Tracking:DatabasePath", _databasePath);
        builder.UseSetting("Tracking:ExportPath", _exportPath);
        builder.UseSetting("Tracking:LogPath", Path.Combine(Path.GetTempPath(), "mcp-track-tokens-tests", "logs"));
        builder.UseSetting("Tracking:QueuePath", Path.Combine(Path.GetTempPath(), "mcp-track-tokens-tests", "queue"));
        builder.UseSetting("Tracking:MigrateOnStartup", "true");
        builder.UseSetting("Tracking:EnableHttpMcp", "false");
        builder.UseSetting("Tracking:BindAddress", "http://127.0.0.1:0");
        builder.UseSetting("IpRateLimiting:EnableEndpointRateLimiting", "false");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await TrackingHost.InitializePersistenceAsync(scope.ServiceProvider).ConfigureAwait(false);
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await apiKeys.CreateAsync(new CreateApiKeyRequestDto { Name = "integration-tests" })
            .ConfigureAwait(false);
        ApiKey = created.ApiKey;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync().ConfigureAwait(false);
        TryDelete(_databasePath);
        TryDeleteDirectory(_exportPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
