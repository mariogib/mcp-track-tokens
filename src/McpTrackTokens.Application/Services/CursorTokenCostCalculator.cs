using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Estimates usage cost from token counts using the configured Cursor rate card.
/// </summary>
public static class CursorTokenCostCalculator
{
    private const decimal Million = 1_000_000m;

    /// <summary>
    /// Resolves the rate row for a model name (exact match, normalized contains, then <c>*</c>/<c>Auto</c>).
    /// </summary>
    public static CursorModelTokenRate? ResolveRate(
        IReadOnlyList<CursorModelTokenRate> rates,
        string? model)
    {
        if (rates.Count == 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim();
        var exact = rates.FirstOrDefault(r =>
            string.Equals(r.Model, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var usageKey = NormalizeModelKey(name);
        if (usageKey.Length > 0)
        {
            CursorModelTokenRate? best = null;
            var bestScore = 0;
            foreach (var rate in rates)
            {
                if (rate.Model is "*" or "Auto")
                {
                    continue;
                }

                var rateKey = NormalizeModelKey(rate.Model);
                if (rateKey.Length == 0)
                {
                    continue;
                }

                if (usageKey == rateKey ||
                    usageKey.Contains(rateKey, StringComparison.Ordinal) ||
                    rateKey.Contains(usageKey, StringComparison.Ordinal))
                {
                    var score = Math.Min(usageKey.Length, rateKey.Length);
                    if (score > bestScore)
                    {
                        best = rate;
                        bestScore = score;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return rates.FirstOrDefault(r => r.Model == "*")
               ?? rates.FirstOrDefault(r =>
                   string.Equals(r.Model, "Auto", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeModelKey(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var n = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.')
            {
                buffer[n++] = char.ToLowerInvariant(ch);
            }
        }

        return new string(buffer[..n]);
    }

    /// <summary>
    /// Estimates cost for one usage record scaled by allocation percentage.
    /// Cached tokens use the cache-read rate; cache-write is reserved for future import fields.
    /// </summary>
    public static decimal Estimate(
        ExternalUsageRecord usage,
        decimal allocationPercentage,
        CursorModelTokenRate rate)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(rate);

        var scale = allocationPercentage <= 0m
            ? 0m
            : allocationPercentage >= 100m
                ? 1m
                : allocationPercentage / 100m;

        var input = (usage.InputTokens ?? 0) * scale;
        var output = (usage.OutputTokens ?? 0) * scale;
        var cached = (usage.CachedInputTokens ?? 0) * scale;
        var reasoning = (usage.ReasoningTokens ?? 0) * scale;

        // If breakdown is missing, price remaining total tokens as input.
        var accounted = input + output + cached + reasoning;
        var total = (usage.TotalTokens ?? 0) * scale;
        if (total > accounted)
        {
            input += total - accounted;
        }

        var cost =
            (input / Million) * rate.InputPerMillion +
            (output / Million) * rate.OutputPerMillion +
            (cached / Million) * rate.CacheReadPerMillion +
            (reasoning / Million) * (rate.ReasoningPerMillion ?? rate.OutputPerMillion);

        return Math.Round(cost, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Seeds Auto-mode Cursor rates (currency per 1M tokens) as a starting rate card.
    /// </summary>
    public static List<CursorModelTokenRate> CreateDefaultRates() =>
    [
        new()
        {
            Model = "Auto",
            InputPerMillion = 1.25m,
            OutputPerMillion = 6.00m,
            CacheReadPerMillion = 0.25m,
            CacheWritePerMillion = 1.25m
        },
        new()
        {
            Model = "*",
            InputPerMillion = 1.25m,
            OutputPerMillion = 6.00m,
            CacheReadPerMillion = 0.25m,
            CacheWritePerMillion = 1.25m
        }
    ];
}
