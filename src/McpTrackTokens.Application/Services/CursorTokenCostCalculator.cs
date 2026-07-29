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
    /// Token buckets after applying a project's usage-allocation percentage.
    /// </summary>
    public readonly record struct ScaledTokenBuckets(
        long InputTokens,
        long OutputTokens,
        long CachedInputTokens,
        long CacheWriteTokens,
        long ReasoningTokens,
        long TotalTokens);

    /// <summary>
    /// Canonicalizes known model aliases (e.g. Cursor <c>default</c>/<c>Auto</c> → <c>auto</c>).
    /// </summary>
    public static string? NormalizeModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var trimmed = model.Trim();
        if (string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        return trimmed;
    }

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

        var name = NormalizeModelName(model) ?? "unknown";
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
                if (rate.Model is "*" ||
                    string.Equals(rate.Model, "Auto", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Returns configured rates or the built-in rate card when no configured rates exist.
    /// </summary>
    public static IReadOnlyList<CursorModelTokenRate> GetEffectiveRates(
        IReadOnlyList<CursorModelTokenRate> configuredRates)
        => configuredRates.Count > 0 ? configuredRates : CreateDefaultRates();

    /// <summary>
    /// Returns the allocation percentage used by reports where zero denotes a full record.
    /// </summary>
    public static decimal GetEffectiveAllocationPercentage(decimal allocationPercentage)
        => allocationPercentage > 0m ? allocationPercentage : 100m;

    /// <summary>
    /// Resolves a model rate and estimates cost, returning zero when no rate matches.
    /// </summary>
    public static decimal EstimateOrZero(
        ExternalUsageRecord usage,
        decimal allocationPercentage,
        IReadOnlyList<CursorModelTokenRate> rates)
    {
        var rate = ResolveRate(rates, usage.Model);
        return rate is null ? 0m : Estimate(usage, allocationPercentage, rate);
    }

    /// <summary>
    /// Normalizes a model name for fuzzy comparison (letters, digits, dots; lowercased).
    /// </summary>
    public static string NormalizeModelKey(string value)
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
    /// Resolves the total token count, preferring the provider-reported total and
    /// otherwise summing all known token buckets.
    /// </summary>
    public static long ResolveTotalTokens(ExternalUsageRecord usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.TotalTokens is > 0)
        {
            return usage.TotalTokens.Value;
        }

        return usage.TotalTokens ??
            (usage.InputTokens ?? 0) +
            (usage.OutputTokens ?? 0) +
            (usage.CachedInputTokens ?? 0) +
            (usage.CacheWriteTokens ?? 0) +
            (usage.ReasoningTokens ?? 0);
    }

    /// <summary>
    /// Applies an attribution percentage to every usage token bucket.
    /// </summary>
    public static ScaledTokenBuckets ScaleTokens(
        ExternalUsageRecord usage,
        decimal allocationPercentage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var input = ScaleTokenCount(usage.InputTokens ?? 0, allocationPercentage);
        var output = ScaleTokenCount(usage.OutputTokens ?? 0, allocationPercentage);
        var cached = ScaleTokenCount(usage.CachedInputTokens ?? 0, allocationPercentage);
        var cacheWrite = ScaleTokenCount(usage.CacheWriteTokens ?? 0, allocationPercentage);
        var reasoning = ScaleTokenCount(usage.ReasoningTokens ?? 0, allocationPercentage);
        var total = ScaleTokenCount(ResolveTotalTokens(usage), allocationPercentage);
        var accounted = input + output + cached + cacheWrite + reasoning;

        // Keep bucket totals aligned with a provider-reported total that is larger
        // than the available component columns.
        if (total > accounted)
        {
            input += total - accounted;
        }

        return new ScaledTokenBuckets(input, output, cached, cacheWrite, reasoning, total);
    }

    /// <summary>
    /// Applies an attribution percentage to one token count.
    /// </summary>
    public static long ScaleTokenCount(long value, decimal allocationPercentage)
    {
        if (value <= 0 || allocationPercentage <= 0m)
        {
            return 0;
        }

        if (allocationPercentage >= 100m)
        {
            return value;
        }

        return (long)Math.Round(value * (allocationPercentage / 100m), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Estimates cost for one usage record scaled by allocation percentage.
    /// Cached input uses the cache-read rate; cache-write tokens use the cache-write rate.
    /// </summary>
    public static decimal Estimate(
        ExternalUsageRecord usage,
        decimal allocationPercentage,
        CursorModelTokenRate rate)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(rate);

        return Estimate(ScaleTokens(usage, allocationPercentage), rate);
    }

    /// <summary>
    /// Estimates cost from already scaled token buckets.
    /// </summary>
    public static decimal Estimate(ScaledTokenBuckets tokens, CursorModelTokenRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        var cost =
            (tokens.InputTokens / Million) * rate.InputPerMillion +
            (tokens.OutputTokens / Million) * rate.OutputPerMillion +
            (tokens.CachedInputTokens / Million) * rate.CacheReadPerMillion +
            (tokens.CacheWriteTokens / Million) * rate.CacheWritePerMillion +
            (tokens.ReasoningTokens / Million) *
            (rate.ReasoningPerMillion ?? rate.OutputPerMillion);

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
