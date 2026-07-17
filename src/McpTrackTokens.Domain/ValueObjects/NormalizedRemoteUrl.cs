using System.Text.RegularExpressions;

namespace McpTrackTokens.Domain.ValueObjects;

/// <summary>
/// A Git remote URL normalized for stable comparison across HTTPS, SSH, and SCP-like forms.
/// </summary>
public sealed partial class NormalizedRemoteUrl : IEquatable<NormalizedRemoteUrl>
{
    /// <summary>
    /// Gets the normalized remote URL value.
    /// </summary>
    public string Value { get; }

    private NormalizedRemoteUrl(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a normalized remote URL from an arbitrary remote string.
    /// </summary>
    /// <param name="remoteUrl">Raw remote URL, or null/whitespace for an empty result.</param>
    /// <returns>A normalized remote URL instance.</returns>
    public static NormalizedRemoteUrl Create(string? remoteUrl)
        => new(Normalize(remoteUrl));

    /// <summary>
    /// Normalizes a Git remote URL.
    /// </summary>
    /// <remarks>
    /// Supports HTTPS, SSH (<c>ssh://</c>), and SCP-like (<c>git@host:owner/repo.git</c>) forms.
    /// Hosts are lowercased, trailing <c>.git</c> is stripped, and credentials are removed.
    /// </remarks>
    /// <param name="remoteUrl">Raw remote URL.</param>
    /// <returns>Normalized remote URL string, or empty when input is null/whitespace.</returns>
    public static string Normalize(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return string.Empty;
        }

        var value = remoteUrl.Trim();

        var scpMatch = ScpLikeRemoteRegex().Match(value);
        if (scpMatch.Success)
        {
            var host = scpMatch.Groups["host"].Value.ToLowerInvariant();
            var scpPath = StripGitSuffix(scpMatch.Groups["path"].Value.TrimStart('/'));
            return $"ssh://{host}/{scpPath}";
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return StripGitSuffix(value).TrimEnd('/');
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "ssh" or "git"))
        {
            return StripGitSuffix(value).TrimEnd('/');
        }

        var hostName = uri.Host.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = StripGitSuffix(uri.AbsolutePath.TrimStart('/')).TrimEnd('/');

        var normalizedScheme = scheme is "http" or "https" ? "https" : "ssh";
        return string.IsNullOrEmpty(path)
            ? $"{normalizedScheme}://{hostName}{port}"
            : $"{normalizedScheme}://{hostName}{port}/{path}";
    }

    private static string StripGitSuffix(string path)
    {
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^4];
        }

        return path;
    }

    [GeneratedRegex(@"^(?<user>[^@/\s]+)@(?<host>[^:/\s]+):(?<path>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ScpLikeRemoteRegex();

    /// <inheritdoc />
    public bool Equals(NormalizedRemoteUrl? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is NormalizedRemoteUrl other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(NormalizedRemoteUrl? left, NormalizedRemoteUrl? right)
        => Equals(left, right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(NormalizedRemoteUrl? left, NormalizedRemoteUrl? right)
        => !Equals(left, right);
}
