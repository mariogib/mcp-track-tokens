using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Infrastructure.Import;

/// <summary>
/// Converts normalized usage DTOs into domain <see cref="ExternalUsageRecord"/> entities.
/// </summary>
public sealed class ExternalUsageNormalizer : IExternalUsageNormalizer
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalUsageRecord>> NormalizeAsync(
        UsageSource source,
        IReadOnlyList<NormalizedUsageRecordDto> records,
        Guid? importBatchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<ExternalUsageRecord>(records.Count);
        var importedAt = DateTimeOffset.UtcNow;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = ParseProvider(record.Provider);
            var totalTokens = UsageTokenTotals.DeriveTotalIfMissing(
                record.TotalTokens,
                record.InputTokens,
                record.OutputTokens,
                record.CachedInputTokens,
                record.CacheWriteTokens,
                record.ReasoningTokens);

            result.Add(ExternalUsageRecord.Create(
                source,
                record.TimestampUtc,
                record.ExternalRecordId,
                record.PeriodStartUtc,
                record.PeriodEndUtc,
                record.UserIdentifier,
                record.Model,
                provider,
                inputTokens: record.InputTokens,
                outputTokens: record.OutputTokens,
                cachedInputTokens: record.CachedInputTokens,
                cacheWriteTokens: record.CacheWriteTokens,
                reasoningTokens: record.ReasoningTokens,
                totalTokens: totalTokens,
                reportedCost: record.ReportedCost ?? 0m,
                currency: record.Currency,
                requestCount: record.RequestCount,
                metadataJson: record.MetadataJson,
                importBatchId: importBatchId,
                importedAtUtc: importedAt));
        }

        return Task.FromResult<IReadOnlyList<ExternalUsageRecord>>(result);
    }

    private static AIProvider? ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AIProvider.Cursor;
        }

        return Enum.TryParse<AIProvider>(value.Trim(), ignoreCase: true, out var provider)
            ? provider
            : AIProvider.Other;
    }
}
