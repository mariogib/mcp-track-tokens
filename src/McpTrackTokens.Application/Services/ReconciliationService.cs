using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Runs attribution over a date range with optional dry-run.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly IExternalUsageRepository _usage;
    private readonly IAttributionEngine _engine;

    public ReconciliationService(
        IExternalUsageRepository usage,
        IAttributionEngine engine)
    {
        _usage = usage;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<ReconciliationResultDto> RunAsync(
        ReconciliationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var records = await _usage
            .ListAsync(request.FromUtc, request.ToUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var attributions = new List<UsageAttributionRow>();
        var allocated = 0;
        var unallocated = 0;
        var skipped = 0;

        foreach (var record in records)
        {
            var proposed = await _engine.ProposeAsync(record, cancellationToken).ConfigureAwait(false);
            var toPersist = new List<UsageAttribution>();

            foreach (var row in proposed)
            {
                if (!request.IncludeLowConfidence &&
                    row.Confidence == AttributionConfidence.Low &&
                    row.AttributionMethod != AttributionMethod.Unallocated)
                {
                    skipped++;
                    continue;
                }

                toPersist.Add(row);
            }

            if (toPersist.Count == 0)
            {
                toPersist.Add(UsageAttribution.Create(
                    record.Id,
                    AttributionMethod.Unallocated,
                    AttributionConfidence.Unallocated,
                    0m,
                    reason: "Low-confidence attributions excluded by reconciliation settings."));
            }

            foreach (var row in toPersist)
            {
                if (row.AttributionMethod == AttributionMethod.Unallocated || row.ProjectId is null)
                {
                    unallocated++;
                }
                else
                {
                    allocated++;
                }

                attributions.Add(Map(row, record.TimestampUtc, record.Model, record.Provider?.ToString()));
            }

            if (!request.DryRun)
            {
                await _engine.PersistAsync(record.Id, toPersist, cancellationToken).ConfigureAwait(false);
            }
        }

        return new ReconciliationResultDto
        {
            DryRun = request.DryRun,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ProcessedCount = records.Count,
            AllocatedCount = allocated,
            UnallocatedCount = unallocated,
            SkippedCount = skipped,
            Attributions = attributions
        };
    }

    private static UsageAttributionRow Map(
        UsageAttribution attribution,
        DateTimeOffset timestampUtc,
        string? model,
        string? provider)
        => new()
        {
            UsageRecordId = attribution.ExternalUsageRecordId,
            AttributionId = attribution.Id,
            ProjectId = attribution.ProjectId,
            TimestampUtc = timestampUtc,
            Model = model,
            Provider = provider,
            AllocatedCost = attribution.AllocatedCost,
            AllocationPercentage = attribution.AllocationPercentage,
            AllocatedTotalTokens = attribution.AllocatedTotalTokens,
            AttributionMethod = attribution.AttributionMethod.ToString(),
            Confidence = attribution.Confidence.ToString(),
            Reason = attribution.Reason
        };
}
