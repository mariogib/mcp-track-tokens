using System.Text.Json;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Application.Services;

internal static class EnumParsing
{
    public static EditorType ParseEditor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EditorType.Other;
        }

        if (Enum.TryParse<EditorType>(value.Trim(), ignoreCase: true, out var editor))
        {
            return editor;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "vscode" or "vs code" or "visualstudio" or "visual-studio-code" => EditorType.VisualStudioCode,
            "cursor" => EditorType.Cursor,
            _ => EditorType.Other
        };
    }

    public static ActivityEventType ParseEventType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Event type is required.", nameof(value));
        }

        if (Enum.TryParse<ActivityEventType>(value.Trim(), ignoreCase: true, out var eventType))
        {
            return eventType;
        }

        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<ActivityEventType>(normalized, ignoreCase: true, out eventType))
        {
            return eventType;
        }

        throw new ArgumentException($"Unsupported event type '{value}'.", nameof(value));
    }

    public static ActivityStatus ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ActivityStatus.Unknown;
        }

        return Enum.TryParse<ActivityStatus>(value.Trim(), ignoreCase: true, out var status)
            ? status
            : ActivityStatus.Unknown;
    }

    public static AIProvider? ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<AIProvider>(value.Trim(), ignoreCase: true, out var provider)
            ? provider
            : AIProvider.Other;
    }

    public static AttributionConfidence CapConfidence(AttributionConfidence confidence, AttributionConfidence maximum)
        => confidence < maximum ? confidence : maximum;
}

internal static class MetadataSerializer
{
    public static string? Serialize(JsonElement? metadata, int maxBytes)
    {
        if (metadata is null)
        {
            return null;
        }

        var json = metadata.Value.GetRawText();
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        if (byteCount > maxBytes)
        {
            throw new ArgumentException($"Metadata exceeds the maximum of {maxBytes} bytes.");
        }

        return json;
    }
}

/// <summary>
/// Application-layer path normalizer wrapping domain value objects.
/// </summary>
public sealed class PathNormalizer : Interfaces.IPathNormalizer
{
    /// <inheritdoc />
    public string Normalize(string? path) => NormalizedPath.Normalize(path);

    /// <inheritdoc />
    public string NormalizeRemoteUrl(string? remoteUrl) => NormalizedRemoteUrl.Normalize(remoteUrl);
}

/// <summary>
/// SHA-256 file hashing used for import deduplication.
/// </summary>
public sealed class FileHashService : Interfaces.IFileHashService
{
    /// <inheritdoc />
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await ComputeSha256Async(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
