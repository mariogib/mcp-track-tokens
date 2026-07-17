using System.Text.RegularExpressions;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Domain.Validation;

/// <summary>
/// Static validation helpers for <see cref="Entities.Project"/> fields.
/// </summary>
public static partial class ProjectValidator
{
    /// <summary>
    /// Maximum allowed project name length.
    /// </summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// Maximum allowed slug length.
    /// </summary>
    public const int MaxSlugLength = 100;

    /// <summary>
    /// Validates that a project name is present and within length limits.
    /// </summary>
    public static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(nameof(name), "Project name is required.");
        }

        if (name.Trim().Length > MaxNameLength)
        {
            throw new ValidationException(nameof(name), $"Project name cannot exceed {MaxNameLength} characters.");
        }
    }

    /// <summary>
    /// Validates that a slug is lowercase alphanumeric with optional hyphens.
    /// </summary>
    public static void ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ValidationException(nameof(slug), "Project slug is required.");
        }

        var trimmed = slug.Trim();
        if (trimmed.Length > MaxSlugLength)
        {
            throw new ValidationException(nameof(slug), $"Project slug cannot exceed {MaxSlugLength} characters.");
        }

        if (!SlugRegex().IsMatch(trimmed))
        {
            throw new ValidationException(
                nameof(slug),
                "Project slug must be lowercase letters, digits, or hyphens, and cannot start or end with a hyphen.");
        }
    }

    /// <summary>
    /// Validates that a currency code is a three-letter alphabetic code.
    /// </summary>
    public static void ValidateCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ValidationException(nameof(currency), "Currency is required.");
        }

        var trimmed = currency.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsLetter))
        {
            throw new ValidationException(nameof(currency), "Currency must be a 3-letter code.");
        }
    }

    /// <summary>
    /// Creates a slug candidate from a display name.
    /// </summary>
    public static string Slugify(string name)
    {
        ValidateName(name);
        var lowered = name.Trim().ToLowerInvariant();
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            throw new ValidationException(nameof(name), "Unable to derive a valid slug from the project name.");
        }

        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        ValidateSlug(slug);
        return slug;
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
