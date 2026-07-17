using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Infrastructure.Background;

/// <summary>
/// Optional hosted service stub that can flush locally queued offline events.
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
        _logger.LogInformation("Queued event flush service watching {QueuePath}", queuePath);

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
        var files = Directory.Exists(queuePath)
            ? Directory.GetFiles(queuePath, "*.json")
            : [];

        if (files.Length == 0)
        {
            return Task.CompletedTask;
        }

        // Stub: persistence/replay is wired by the host when ingestion endpoints are ready.
        _logger.LogDebug("Found {Count} queued event file(s); flush stub idle.", files.Length);
        return Task.CompletedTask;
    }
}
