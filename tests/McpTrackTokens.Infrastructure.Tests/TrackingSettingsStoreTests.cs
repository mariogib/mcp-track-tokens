using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DependencyInjection;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.DependencyInjection;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class TrackingSettingsStoreTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mtt-settings-{Guid.NewGuid():N}.db");
    private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"mtt-settings-exports-{Guid.NewGuid():N}");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_exportPath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tracking:DatabaseProvider"] = "Sqlite",
                ["Tracking:DatabasePath"] = _dbPath,
                ["Tracking:ExportPath"] = _exportPath,
                ["Tracking:LogPath"] = Path.Combine(Path.GetTempPath(), "mtt-logs"),
                ["Tracking:QueuePath"] = Path.Combine(Path.GetTempPath(), "mtt-queue"),
                ["Tracking:MigrateOnStartup"] = "true",
                ["Tracking:AutoCreateProjects"] = "true"
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddLogging();
        collection.AddApplication();
        collection.AddInfrastructure(configuration);
        _services = collection.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        TryDelete(_dbPath);
        TryDeleteDirectory(_exportPath);
        TryDelete(Path.Combine(Path.GetDirectoryName(_dbPath)!, "cursor-token-rates.json"));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsTrackingPreferences()
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITrackingSettingsStore>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TrackingOptions>>().Value;

        options.AutoCreateProjects.Should().BeTrue();
        options.InactivityThresholdMinutes = 42;
        options.SessionInactivityCloseMinutes = 90;
        options.DefaultCurrency = "EUR";
        options.CursorSubscriptionAmount = 20m;
        options.CursorAllocationMethod = AllocationRuleType.ProportionalTimeAllocation;
        options.StorePromptContent = true;
        options.AutoCreateProjects = false;
        options.EstimateCostFromTokenRates = true;
        options.DataRetentionDays = 180;

        await store.SaveAsync(options);

        var reloaded = new TrackingOptions
        {
            DatabasePath = options.DatabasePath,
            AutoCreateProjects = true
        };
        await store.LoadIntoAsync(reloaded);

        reloaded.InactivityThresholdMinutes.Should().Be(42);
        reloaded.SessionInactivityCloseMinutes.Should().Be(90);
        reloaded.DefaultCurrency.Should().Be("EUR");
        reloaded.CursorSubscriptionAmount.Should().Be(20m);
        reloaded.CursorAllocationMethod.Should().Be(AllocationRuleType.ProportionalTimeAllocation);
        reloaded.StorePromptContent.Should().BeTrue();
        reloaded.AutoCreateProjects.Should().BeFalse();
        reloaded.EstimateCostFromTokenRates.Should().BeTrue();
        reloaded.DataRetentionDays.Should().Be(180);
        reloaded.CursorTokenRates.Should().NotBeEmpty();
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
            // ignore
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
            // ignore
        }
    }
}
