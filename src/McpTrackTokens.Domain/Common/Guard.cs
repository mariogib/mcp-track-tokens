using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.Common;

/// <summary>
/// Lightweight argument validation helpers for the domain layer.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Throws when <paramref name="value"/> is null.
    /// </summary>
    public static T AgainstNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be null.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is null or whitespace.
    /// </summary>
    public static string AgainstNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be null or whitespace.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is an empty string (null is allowed).
    /// </summary>
    public static string? AgainstEmpty(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is not null && value.Length == 0)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be empty.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is <see cref="Guid.Empty"/>.
    /// </summary>
    public static Guid AgainstEmpty(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be an empty GUID.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is negative.
    /// </summary>
    public static int AgainstNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is negative.
    /// </summary>
    public static long AgainstNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is negative.
    /// </summary>
    public static decimal AgainstNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is less than or equal to zero.
    /// </summary>
    public static int AgainstZeroOrNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
        {
            throw new ValidationException(paramName ?? "value", "Value must be greater than zero.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is outside the inclusive range.
    /// </summary>
    public static T AgainstOutOfRange<T>(
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new ValidationException(
                paramName ?? "value",
                $"Value must be between {min} and {max} (inclusive).");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="condition"/> is false.
    /// </summary>
    public static void Against(
        bool condition,
        string message,
        string? paramName = null)
    {
        if (condition)
        {
            throw new ValidationException(paramName ?? "value", message);
        }
    }

    /// <summary>
    /// Throws when the default value of a value type is supplied.
    /// </summary>
    public static DateTimeOffset AgainstDefault(
        DateTimeOffset value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == default)
        {
            throw new ValidationException(paramName ?? "value", "Value cannot be the default DateTimeOffset.");
        }

        return value;
    }
}
