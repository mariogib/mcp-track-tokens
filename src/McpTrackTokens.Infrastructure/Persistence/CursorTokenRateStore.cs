using System.Text.Json;
using Microsoft.Extensions.Logging;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;

namespace McpTrackTokens.Infrastructure.Persistence;

/// <summary>
/// JSON file store for Cursor token rates, saved next to the SQLite database.
/// </summary>
public sealed class CursorTokenRateStore : ICursorTokenRateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<CursorTokenRateStore> _logger;

    public CursorTokenRateStore(ILogger<CursorTokenRateStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LoadIntoAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = ResolvePath(options);
        if (!File.Exists(path))
        {
            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CreateDefaultRates();
            }

            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer
                .DeserializeAsync<CursorTokenRatesDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                return;
            }

            options.EstimateCostFromTokenRates = document.EstimateCostFromTokenRates;
            options.CursorTokenRates = NormalizeRates(document.Rates);
            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CreateDefaultRates();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load Cursor token rates from {Path}", path);
            if (options.CursorTokenRates.Count == 0)
            {
                options.CursorTokenRates = CreateDefaultRates();
            }
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = ResolvePath(options);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new CursorTokenRatesDocument
        {
            EstimateCostFromTokenRates = options.EstimateCostFromTokenRates,
            Rates = NormalizeRates(options.CursorTokenRates)
        };

        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, document, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolvePath(TrackingOptions options)
    {
        var databasePath = options.GetResolvedDatabasePath();
        var directory = Path.GetDirectoryName(databasePath)
            ?? TrackingOptions.ExpandPath("~/.mcp-track-tokens");
        return Path.Combine(directory, "cursor-token-rates.json");
    }

    private static List<CursorModelTokenRate> NormalizeRates(IEnumerable<CursorModelTokenRate>? rates)
    {
        if (rates is null)
        {
            return [];
        }

        return rates
            .Where(r => !string.IsNullOrWhiteSpace(r.Model))
            .Select(r => new CursorModelTokenRate
            {
                Model = r.Model.Trim(),
                InputPerMillion = Math.Max(0m, r.InputPerMillion),
                OutputPerMillion = Math.Max(0m, r.OutputPerMillion),
                CacheReadPerMillion = Math.Max(0m, r.CacheReadPerMillion),
                CacheWritePerMillion = Math.Max(0m, r.CacheWritePerMillion),
                ReasoningPerMillion = r.ReasoningPerMillion is null
                    ? null
                    : Math.Max(0m, r.ReasoningPerMillion.Value)
            })
            .GroupBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(r => r.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Seeds Auto-mode Cursor rates (USD per 1M tokens) as a starting rate card.
    /// </summary>
    public static List<CursorModelTokenRate> CreateDefaultRates()
        => CursorTokenCostCalculator.CreateDefaultRates();

    private sealed class CursorTokenRatesDocument
    {
        public bool EstimateCostFromTokenRates { get; set; }

        public List<CursorModelTokenRate> Rates { get; set; } = [];
    }
}
