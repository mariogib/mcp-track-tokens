using System.Globalization;
using System.Text.Json;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Infrastructure.Import;

/// <summary>
/// Detects Cursor usage export formats (CSV vs JSON).
/// </summary>
public sealed class CursorUsageFormatDetector : ICursorUsageFormatDetector
{
    /// <inheritdoc />
    public async Task<UsageSource> DetectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await DetectAsync(stream, Path.GetFileName(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UsageSource> DetectAsync(
        Stream content,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension is ".json" or ".jsonl")
        {
            return UsageSource.CursorJson;
        }

        if (extension is ".csv" or ".tsv")
        {
            return UsageSource.CursorCsv;
        }

        // Peek at content.
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var reader = new StreamReader(content, leaveOpen: true);
        var buffer = new char[512];
        var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var sample = new string(buffer, 0, read).TrimStart();
        if (sample.StartsWith('{') || sample.StartsWith('['))
        {
            return UsageSource.CursorJson;
        }

        // Try JSON parse of first non-empty line for JSONL.
        var firstLine = sample.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            try
            {
                using var _ = JsonDocument.Parse(firstLine);
                return UsageSource.CursorJson;
            }
            catch (JsonException)
            {
                // Not JSON.
            }
        }

        return UsageSource.CursorCsv;
    }
}
