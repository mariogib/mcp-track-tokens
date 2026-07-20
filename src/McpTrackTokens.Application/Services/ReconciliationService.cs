using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Runs attribution over imported usage rows with Total Tokens &gt; 0.
/// Each eligible row is linked to the closest prompt at or before its timestamp
/// (second precision); the same prompt may receive many usage attributions.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly IExternalUsageRepository _usage;
    private readonly IAttributionEngine _engine;
    private readonly TrackingOptions _options;

    public ReconciliationService(
        IExternalUsageRepository usage,
        IAttributionEngine engine,
        IOptions<TrackingOptions> options)
    {
        _usage = usage;
        _engine = engine;
        _options = options.Value;
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

        var rates = _options.CursorTokenRates.Count > 0
            ? _options.CursorTokenRates
            : CursorTokenCostCalculator.CreateDefaultRates();

        var attributions = new List<UsageAttributionRow>();
        var allocated = 0;
        var unallocated = 0;
        var skipped = 0;

        foreach (var record in records)
        {
            // Only reconcile rows with Total Tokens > 0 (Included/Free cost may be 0).
            if (AttributionEngine.ResolveTotalTokens(record) <= 0)
            {
                skipped++;
                continue;
            }

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
                    allocatedCost: 0m,
                    allocatedTotalTokens: AttributionEngine.ResolveTotalTokens(record),
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

                attributions.Add(Map(row, record, rates));
            }

            if (!request.DryRun)
            {
                await _engine.PersistAsync(record.Id, toPersist, cancellationToken).ConfigureAwait(false);
            }
        }

        var eligible = records.Count(r => AttributionEngine.ResolveTotalTokens(r) > 0);
        var stillUnallocated = attributions
            .Where(a =>
                a.ProjectId is null ||
                string.Equals(a.AttributionMethod, nameof(AttributionMethod.Unallocated), StringComparison.Ordinal))
            .OrderByDescending(a => a.TimestampUtc)
            .ToList();

        return new ReconciliationResultDto
        {
            DryRun = request.DryRun,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ProcessedCount = eligible,
            AllocatedCount = allocated,
            UnallocatedCount = unallocated,
            SkippedCount = skipped,
            Attributions = attributions,
            Unallocated = stillUnallocated
        };
    }

    private static UsageAttributionRow Map(
        UsageAttribution attribution,
        ExternalUsageRecord record,
        IReadOnlyList<CursorModelTokenRate> rates)
    {
        var rate = CursorTokenCostCalculator.ResolveRate(rates, record.Model);
        var percentage = attribution.AllocationPercentage > 0m
            ? attribution.AllocationPercentage
            : 100m;
        var calculated = rate is null
            ? 0m
            : CursorTokenCostCalculator.Estimate(record, percentage, rate);

        return new()
        {
            UsageRecordId = attribution.ExternalUsageRecordId,
            AttributionId = attribution.Id,
            ProjectId = attribution.ProjectId,
            ActivityEventId = attribution.ActivityEventId,
            TimestampUtc = record.TimestampUtc,
            Model = record.Model,
            Provider = record.Provider?.ToString(),
            AllocatedCost = attribution.AllocatedCost,
            CalculatedTokenCost = calculated,
            AllocationPercentage = attribution.AllocationPercentage,
            AllocatedTotalTokens = attribution.AllocatedTotalTokens,
            AttributionMethod = attribution.AttributionMethod.ToString(),
            Confidence = attribution.Confidence.ToString(),
            Reason = attribution.Reason
        };
    }
}
