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
            var totalTokens = record.TotalTokens;
            if (totalTokens is null && (record.InputTokens is not null || record.OutputTokens is not null))
            {
                totalTokens = (record.InputTokens ?? 0) + (record.OutputTokens ?? 0)
                    + (record.CachedInputTokens ?? 0) + (record.ReasoningTokens ?? 0);
            }

            result.Add(ExternalUsageRecord.Create(
                source,
                record.TimestampUtc,
                record.ExternalRecordId,
                record.PeriodStartUtc,
                record.PeriodEndUtc,
                record.UserIdentifier,
                record.Model,
                provider,
                record.InputTokens,
                record.OutputTokens,
                record.CachedInputTokens,
                record.ReasoningTokens,
                totalTokens,
                record.ReportedCost,
                record.Currency,
                record.RequestCount,
                record.MetadataJson,
                importBatchId,
                importedAt));
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
