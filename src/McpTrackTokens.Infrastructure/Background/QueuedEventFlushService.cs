using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;

namespace McpTrackTokens.Infrastructure.Background;

/// <summary>
/// Enforces <see cref="TrackingOptions.MaxQueuedEvents"/> on the offline queue directory
/// and watches for queued event files (replay remains host/client driven).
/// </summary>
public sealed class QueuedEventFlushService : BackgroundService
{
    private readonly ILogger<QueuedEventFlushService> _logger;
    private readonly TrackingOptions _options;

    public QueuedEventFlushService(
        ILogger<QueuedEventFlushService> logger,
        IOptions<TrackingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queuePath = TrackingOptions.ExpandPath(_options.QueuePath);
        Directory.CreateDirectory(queuePath);
        _logger.LogInformation(
            "Queued event flush service watching {QueuePath} (max {MaxQueuedEvents})",
            queuePath,
            _options.MaxQueuedEvents);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushOnceAsync(queuePath, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Queued event flush iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    private Task FlushOnceAsync(string queuePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(queuePath))
        {
            return Task.CompletedTask;
        }

        var dropped = OfflineQueueDisk.TrimToMax(queuePath, _options.MaxQueuedEvents);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Trimmed {Dropped} offline queued event(s) to enforce MaxQueuedEvents={Max}.",
                dropped,
                _options.MaxQueuedEvents);
        }

        var queued = OfflineQueueDisk.CountEvents(queuePath);
        var jsonStubs = Directory.GetFiles(queuePath, "*.json").Length;
        if (queued > 0)
        {
            _logger.LogDebug(
                "Offline queue has {Queued} event(s) ({JsonStubs} *.json stub file(s)); clients flush JSONL on reconnect.",
                queued,
                jsonStubs);
        }

        return Task.CompletedTask;
    }
}
