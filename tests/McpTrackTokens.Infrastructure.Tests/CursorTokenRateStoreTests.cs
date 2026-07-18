using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class CursorTokenRateStoreTests
{
    [Fact]
    public async Task Save_then_load_round_trips_rates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtt-rates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new TrackingOptions
            {
                DatabasePath = Path.Combine(dir, "test.db"),
                EstimateCostFromTokenRates = true,
                CursorTokenRates =
                [
                    new CursorModelTokenRate
                    {
                        Model = "claude-4.5-sonnet",
                        InputPerMillion = 3m,
                        OutputPerMillion = 15m,
                        CacheReadPerMillion = 0.3m,
                        CacheWritePerMillion = 3.75m,
                        ReasoningPerMillion = 15m
                    }
                ]
            };

            var store = new CursorTokenRateStore(NullLogger<CursorTokenRateStore>.Instance);
            await store.SaveAsync(options);

            var loaded = new TrackingOptions { DatabasePath = options.DatabasePath };
            await store.LoadIntoAsync(loaded);

            loaded.EstimateCostFromTokenRates.Should().BeTrue();
            loaded.CursorTokenRates.Should().ContainSingle();
            loaded.CursorTokenRates[0].Model.Should().Be("claude-4.5-sonnet");
            loaded.CursorTokenRates[0].InputPerMillion.Should().Be(3m);
            loaded.CursorTokenRates[0].OutputPerMillion.Should().Be(15m);
            loaded.CursorTokenRates[0].ReasoningPerMillion.Should().Be(15m);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_without_file_seeds_default_auto_rates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtt-rates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new TrackingOptions
            {
                DatabasePath = Path.Combine(dir, "missing.db")
            };
            var store = new CursorTokenRateStore(NullLogger<CursorTokenRateStore>.Instance);
            await store.LoadIntoAsync(options);

            options.CursorTokenRates.Should().NotBeEmpty();
            options.CursorTokenRates.Should().Contain(r => r.Model == "Auto");
            options.CursorTokenRates.Should().Contain(r => r.Model == "*");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
