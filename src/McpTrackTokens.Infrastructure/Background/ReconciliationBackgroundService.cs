using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;

namespace McpTrackTokens.Infrastructure.Background;

/// <summary>
/// Optional background reconciliation loop for unallocated usage.
/// </summary>
public sealed class ReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReconciliationBackgroundService> _logger;

    public ReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reconciliation background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciliation background iteration failed.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider.GetService<IReconciliationService>();
        if (reconciliation is null)
        {
            _logger.LogDebug("IReconciliationService is not registered; skipping reconciliation pass.");
            return;
        }

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-7);
        var result = await reconciliation
            .RunAsync(
                new ReconciliationRequestDto
                {
                    FromUtc = from,
                    ToUtc = to,
                    DryRun = false,
                    IncludeLowConfidence = false
                },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Reconciliation processed {Processed} rows ({Allocated} allocated, {Unallocated} unallocated).",
            result.ProcessedCount,
            result.AllocatedCount,
            result.UnallocatedCount);
    }
}
