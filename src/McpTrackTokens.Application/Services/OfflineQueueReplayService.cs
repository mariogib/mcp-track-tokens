using System.Text.Json;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Reads offline queue JSONL / *.json stubs and ingests them through <see cref="IEventIngestionService"/>.
/// </summary>
public sealed class OfflineQueueReplayService : IOfflineQueueReplayService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IEventIngestionService _ingestion;
    private readonly TrackingOptions _options;

    public OfflineQueueReplayService(
        IEventIngestionService ingestion,
        IOptions<TrackingOptions> options)
    {
        _ingestion = ingestion;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<OfflineQueueReplayResultDto> ReplayAsync(CancellationToken cancellationToken = default)
    {
        var queuePath = TrackingOptions.ExpandPath(_options.QueuePath);
        if (!Directory.Exists(queuePath))
        {
            return new OfflineQueueReplayResultDto();
        }

        var attempted = 0;
        var flushed = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(queuePath, "*.jsonl").OrderBy(static p => p))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (fileAttempted, fileFlushed, fileFailed, fileErrors) =
                await ReplayJsonlAsync(file, cancellationToken).ConfigureAwait(false);
            attempted += fileAttempted;
            flushed += fileFlushed;
            failed += fileFailed;
            errors.AddRange(fileErrors);
        }

        foreach (var file in Directory.EnumerateFiles(queuePath, "*.json").OrderBy(static p => p))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var dto = JsonSerializer.Deserialize<IngestEventDto>(json, JsonOptions);
                if (dto is null || string.IsNullOrWhiteSpace(dto.EventType))
                {
                    failed++;
                    errors.Add($"{Path.GetFileName(file)}: invalid event JSON.");
                    continue;
                }

                await _ingestion.IngestAsync(dto, cancellationToken).ConfigureAwait(false);
                File.Delete(file);
                flushed++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return new OfflineQueueReplayResultDto
        {
            Attempted = attempted,
            Flushed = flushed,
            Failed = failed,
            Remaining = OfflineQueueDisk.CountEvents(queuePath),
            Errors = errors.Take(25).ToList()
        };
    }

    private async Task<(int Attempted, int Flushed, int Failed, List<string> Errors)> ReplayJsonlAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        string[] lines;
        try
        {
            lines = (await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false))
                .Where(static l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
        }
        catch (Exception ex)
        {
            return (0, 0, 1, [$"{Path.GetFileName(filePath)}: {ex.Message}"]);
        }

        if (lines.Length == 0)
        {
            return (0, 0, 0, []);
        }

        var remaining = new List<string>();
        var flushed = 0;
        var failed = 0;
        var errors = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];
            try
            {
                var dto = JsonSerializer.Deserialize<IngestEventDto>(line, JsonOptions);
                if (dto is null || string.IsNullOrWhiteSpace(dto.EventType))
                {
                    remaining.Add(line);
                    failed++;
                    errors.Add($"{Path.GetFileName(filePath)} line {i + 1}: invalid event JSON.");
                    continue;
                }

                await _ingestion.IngestAsync(dto, cancellationToken).ConfigureAwait(false);
                flushed++;
            }
            catch (Exception ex)
            {
                remaining.Add(line);
                failed++;
                errors.Add($"{Path.GetFileName(filePath)} line {i + 1}: {ex.Message}");
            }
        }

        await File.WriteAllTextAsync(
                filePath,
                remaining.Count == 0 ? string.Empty : string.Join('\n', remaining) + "\n",
                cancellationToken)
            .ConfigureAwait(false);

        return (lines.Length, flushed, failed, errors);
    }
}
