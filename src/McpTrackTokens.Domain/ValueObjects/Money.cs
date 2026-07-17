using System.Globalization;
using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.ValueObjects;

/// <summary>
/// Monetary amount with an ISO-style currency code.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>
    /// Gets the monetary amount.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// Gets the currency code (typically ISO 4217, three letters).
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Initializes a new <see cref="Money"/> value.
    /// </summary>
    /// <param name="amount">Amount.</param>
    /// <param name="currency">Currency code.</param>
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = Guard.AgainstNullOrWhiteSpace(currency).Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Creates a zero amount in the specified currency.
    /// </summary>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>
    /// Adds two money values of the same currency.
    /// </summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Subtracts another money value of the same currency.
    /// </summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplies the amount by a scalar factor.
    /// </summary>
    public Money Multiply(decimal factor)
        => new(Amount * factor, Currency);

    /// <summary>
    /// Allocates this amount across the given weights, ensuring the parts sum exactly to the original amount.
    /// Any remainder from rounding is applied to the last non-zero-weight item (or the last item).
    /// </summary>
    /// <param name="weights">Relative weights (may be zero). Must contain at least one item.</param>
    /// <param name="decimals">Number of decimal places for each allocated part.</param>
    /// <returns>Allocated money values in the same order as <paramref name="weights"/>.</returns>
    public IReadOnlyList<Money> AllocateByWeights(IReadOnlyList<decimal> weights, int decimals = 2)
    {
        Guard.AgainstNull(weights);
        Guard.Against(weights.Count == 0, "At least one weight is required.", nameof(weights));
        Guard.AgainstNegative(decimals);

        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
        {
            throw new ValidationException(nameof(weights), "Total weight must be greater than zero.");
        }

        var results = new Money[weights.Count];
        var allocated = 0m;
        var lastIndex = FindRemainderIndex(weights);

        for (var i = 0; i < weights.Count; i++)
        {
            if (i == lastIndex)
            {
                continue;
            }

            var share = Math.Round(Amount * (weights[i] / totalWeight), decimals, MidpointRounding.AwayFromZero);
            results[i] = new Money(share, Currency);
            allocated += share;
        }

        results[lastIndex] = new Money(Amount - allocated, Currency);
        return results;
    }

    /// <summary>
    /// Allocates this amount by percentages that must total 100, with remainder applied to the last item.
    /// </summary>
    /// <param name="percentages">Percentages (0–100) that should sum to 100.</param>
    /// <param name="decimals">Number of decimal places for each allocated part.</param>
    public IReadOnlyList<Money> AllocateByPercentages(IReadOnlyList<Percentage> percentages, int decimals = 2)
    {
        Guard.AgainstNull(percentages);
        Guard.Against(percentages.Count == 0, "At least one percentage is required.", nameof(percentages));

        var rounded = Percentage.EnsureSumTo100(percentages.Select(p => p.Value).ToArray());
        var weights = rounded.Select(p => p.Value).ToArray();
        return AllocateByWeights(weights, decimals);
    }

    private static int FindRemainderIndex(IReadOnlyList<decimal> weights)
    {
        for (var i = weights.Count - 1; i >= 0; i--)
        {
            if (weights[i] != 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                nameof(Currency),
                $"Currency mismatch: '{Currency}' vs '{other.Currency}'.");
        }
    }

    /// <inheritdoc />
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    public bool Equals(Money other)
        => Amount == other.Amount
           && string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is Money other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Amount, StringComparer.OrdinalIgnoreCase.GetHashCode(Currency));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.##} {Currency}");

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Money left, Money right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Money left, Money right) => !left.Equals(right);

    /// <summary>Addition operator.</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtraction operator.</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Scalar multiplication operator.</summary>
    public static Money operator *(Money left, decimal right) => left.Multiply(right);
}
