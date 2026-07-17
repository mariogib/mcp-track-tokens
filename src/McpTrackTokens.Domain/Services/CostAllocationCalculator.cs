using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Services;

/// <summary>
/// A weighted allocation target identified by an opaque key.
/// </summary>
/// <param name="Key">Caller-defined identifier (for example a project id).</param>
/// <param name="Weight">Relative weight used for proportional allocation.</param>
public readonly record struct AllocationWeight(string Key, decimal Weight);

/// <summary>
/// Result of allocating an amount or percentage across targets.
/// </summary>
/// <param name="Key">Caller-defined identifier.</param>
/// <param name="Percentage">Allocated percentage (sums to 100 across the set).</param>
/// <param name="Amount">Allocated decimal amount (sums to the original total).</param>
public readonly record struct AllocationShare(string Key, Percentage Percentage, decimal Amount);

/// <summary>
/// Proportional allocation of decimal amounts with remainder handling so totals remain exact.
/// </summary>
public sealed class CostAllocationCalculator
{
    /// <summary>
    /// Allocates <paramref name="totalAmount"/> across <paramref name="weights"/> proportionally.
    /// Percentages sum to exactly 100 and amounts sum to exactly <paramref name="totalAmount"/>.
    /// Remainder from rounding is applied to the last non-zero-weight item.
    /// </summary>
    /// <param name="totalAmount">Total amount to allocate (must be non-negative).</param>
    /// <param name="weights">Relative weights. Zero-weight items receive zero.</param>
    /// <param name="decimals">Decimal places for amount rounding.</param>
    /// <param name="percentageDecimals">Decimal places for percentage rounding.</param>
    public IReadOnlyList<AllocationShare> AllocateProportionally(
        decimal totalAmount,
        IReadOnlyList<AllocationWeight> weights,
        int decimals = 2,
        int percentageDecimals = 2)
    {
        Guard.AgainstNull(weights);
        Guard.Against(weights.Count == 0, "At least one allocation target is required.", nameof(weights));
        Guard.AgainstNegative(totalAmount);
        Guard.AgainstNegative(decimals);
        Guard.AgainstNegative(percentageDecimals);

        foreach (var weight in weights)
        {
            Guard.AgainstNullOrWhiteSpace(weight.Key);
            Guard.AgainstNegative(weight.Weight);
        }

        var totalWeight = weights.Sum(w => w.Weight);
        if (totalWeight <= 0)
        {
            throw new AttributionException("Total allocation weight must be greater than zero.");
        }

        var rawPercentages = weights.Select(w => w.Weight / totalWeight * 100m).ToArray();
        var percentages = Percentage.EnsureSumTo100(rawPercentages, percentageDecimals);

        var amounts = new decimal[weights.Count];
        var allocated = 0m;
        var lastIndex = FindRemainderIndex(weights);

        for (var i = 0; i < weights.Count; i++)
        {
            if (i == lastIndex)
            {
                continue;
            }

            var amount = Math.Round(
                totalAmount * percentages[i].ToRatio(),
                decimals,
                MidpointRounding.AwayFromZero);
            amounts[i] = amount;
            allocated += amount;
        }

        amounts[lastIndex] = totalAmount - allocated;

        var results = new AllocationShare[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            results[i] = new AllocationShare(weights[i].Key, percentages[i], amounts[i]);
        }

        return results;
    }

    /// <summary>
    /// Allocates using explicit percentages that must normalize to 100.
    /// </summary>
    public IReadOnlyList<AllocationShare> AllocateByPercentages(
        decimal totalAmount,
        IReadOnlyList<(string Key, decimal Percentage)> targets,
        int decimals = 2,
        int percentageDecimals = 2)
    {
        Guard.AgainstNull(targets);
        Guard.Against(targets.Count == 0, "At least one allocation target is required.", nameof(targets));

        var weights = targets
            .Select(t => new AllocationWeight(t.Key, t.Percentage))
            .ToArray();

        return AllocateProportionally(totalAmount, weights, decimals, percentageDecimals);
    }

    private static int FindRemainderIndex(IReadOnlyList<AllocationWeight> weights)
    {
        for (var i = weights.Count - 1; i >= 0; i--)
        {
            if (weights[i].Weight != 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }
}
