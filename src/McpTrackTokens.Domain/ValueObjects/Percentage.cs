using System.Globalization;
using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.ValueObjects;

/// <summary>
/// A percentage value constrained to the inclusive range 0–100.
/// </summary>
public readonly struct Percentage : IEquatable<Percentage>, IComparable<Percentage>
{
    /// <summary>
    /// Gets the percentage value.
    /// </summary>
    public decimal Value { get; }

    /// <summary>
    /// Initializes a new <see cref="Percentage"/> value.
    /// </summary>
    /// <param name="value">Percentage between 0 and 100 inclusive.</param>
    public Percentage(decimal value)
    {
        Guard.AgainstOutOfRange(value, 0m, 100m);
        Value = value;
    }

    /// <summary>
    /// Creates a percentage from a ratio (0–1).
    /// </summary>
    public static Percentage FromRatio(decimal ratio)
    {
        Guard.AgainstOutOfRange(ratio, 0m, 1m);
        return new Percentage(ratio * 100m);
    }

    /// <summary>
    /// Converts this percentage to a 0–1 ratio.
    /// </summary>
    public decimal ToRatio() => Value / 100m;

    /// <summary>
    /// Rounds the percentage to the specified number of decimal places.
    /// </summary>
    public Percentage Round(int decimals = 2)
    {
        Guard.AgainstNegative(decimals);
        return new Percentage(Math.Round(Value, decimals, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Rounds a set of percentages so they sum exactly to 100, applying any remainder to the last non-zero item.
    /// </summary>
    /// <param name="values">Raw percentage values (need not already sum to 100).</param>
    /// <param name="decimals">Decimal places for each rounded percentage.</param>
    /// <returns>Percentages that sum to exactly 100.</returns>
    public static IReadOnlyList<Percentage> EnsureSumTo100(IReadOnlyList<decimal> values, int decimals = 2)
    {
        Guard.AgainstNull(values);
        Guard.Against(values.Count == 0, "At least one percentage is required.", nameof(values));
        Guard.AgainstNegative(decimals);

        foreach (var value in values)
        {
            Guard.AgainstOutOfRange(value, 0m, 100m);
        }

        var total = values.Sum();
        if (total <= 0)
        {
            throw new ValidationException(nameof(values), "Total percentage weight must be greater than zero.");
        }

        // Scale to 100 if the caller supplied relative weights that do not already sum to 100.
        var scaled = total == 100m
            ? values.ToArray()
            : values.Select(v => v / total * 100m).ToArray();

        var results = new Percentage[scaled.Length];
        var allocated = 0m;
        var lastIndex = FindRemainderIndex(scaled);

        for (var i = 0; i < scaled.Length; i++)
        {
            if (i == lastIndex)
            {
                continue;
            }

            var rounded = Math.Round(scaled[i], decimals, MidpointRounding.AwayFromZero);
            results[i] = new Percentage(rounded);
            allocated += rounded;
        }

        var remainder = Math.Round(100m - allocated, decimals, MidpointRounding.AwayFromZero);
        if (remainder < 0m || remainder > 100m)
        {
            throw new ValidationException(nameof(values), "Unable to normalize percentages to sum to 100.");
        }

        results[lastIndex] = new Percentage(remainder);
        return results;
    }

    /// <summary>
    /// Distributes equal percentages across <paramref name="count"/> items that sum to 100.
    /// </summary>
    public static IReadOnlyList<Percentage> EqualParts(int count, int decimals = 2)
    {
        Guard.AgainstZeroOrNegative(count);
        var equal = Enumerable.Repeat(100m / count, count).ToArray();
        return EnsureSumTo100(equal, decimals);
    }

    private static int FindRemainderIndex(IReadOnlyList<decimal> values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (values[i] != 0)
            {
                return i;
            }
        }

        return values.Count - 1;
    }

    /// <inheritdoc />
    public int CompareTo(Percentage other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(Percentage other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Percentage other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override string ToString()
        => Value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Percentage left, Percentage right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Percentage left, Percentage right) => !left.Equals(right);
}
