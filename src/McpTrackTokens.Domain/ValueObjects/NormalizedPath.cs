namespace McpTrackTokens.Domain.ValueObjects;

/// <summary>
/// A filesystem path normalized for stable comparison across Windows and Unix.
/// </summary>
public sealed class NormalizedPath : IEquatable<NormalizedPath>
{
    /// <summary>
    /// Gets the normalized path value.
    /// </summary>
    public string Value { get; }

    private NormalizedPath(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a normalized path from an arbitrary path string.
    /// </summary>
    /// <param name="path">Raw path, or null/whitespace for an empty result.</param>
    /// <returns>A normalized path instance.</returns>
    public static NormalizedPath Create(string? path)
        => new(Normalize(path));

    /// <summary>
    /// Normalizes a filesystem path for comparison.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    /// <item>Trims whitespace and trailing directory separators.</item>
    /// <item>Converts backslashes to forward slashes.</item>
    /// <item>Uppercases Windows drive letters.</item>
    /// <item>Preserves UNC share prefixes (<c>//server/share</c>).</item>
    /// <item>Uses ordinal case-insensitive comparison semantics for Windows-style paths.</item>
    /// </list>
    /// </remarks>
    /// <param name="path">Raw path.</param>
    /// <returns>Normalized path string, or empty when input is null/whitespace.</returns>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        var unified = trimmed.Replace('\\', '/');

        while (unified.Contains("//", StringComparison.Ordinal) && !unified.StartsWith("//", StringComparison.Ordinal))
        {
            unified = unified.Replace("//", "/", StringComparison.Ordinal);
        }

        // Collapse accidental duplicate separators after the UNC prefix.
        if (unified.StartsWith("//", StringComparison.Ordinal))
        {
            var prefix = "//";
            var remainder = unified[2..];
            while (remainder.Contains("//", StringComparison.Ordinal))
            {
                remainder = remainder.Replace("//", "/", StringComparison.Ordinal);
            }

            unified = prefix + remainder;
        }

        unified = TrimTrailingSeparators(unified);

        if (unified.Length >= 2 && char.IsLetter(unified[0]) && unified[1] == ':')
        {
            unified = char.ToUpperInvariant(unified[0]) + unified[1..];
        }

        return unified;
    }

    private static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // Keep root paths like "C:" or "/" or "//server/share" meaningful.
        if (path is "/" or "\\" )
        {
            return "/";
        }

        if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return path;
        }

        var result = path.TrimEnd('/');

        // UNC root "//server/share" should keep its two path segments.
        if (result.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = result[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
            {
                return result;
            }
        }

        return result.Length == 0 ? "/" : result;
    }

    /// <inheritdoc />
    public bool Equals(NormalizedPath? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is NormalizedPath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(NormalizedPath? left, NormalizedPath? right)
        => Equals(left, right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(NormalizedPath? left, NormalizedPath? right)
        => !Equals(left, right);
}
