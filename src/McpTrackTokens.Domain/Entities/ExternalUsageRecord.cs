using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// An imported usage or cost record from an external provider.
/// </summary>
public sealed class ExternalUsageRecord : EntityBase
{
    /// <summary>
    /// Gets or sets the usage source.
    /// </summary>
    public UsageSource Source { get; set; }

    /// <summary>
    /// Gets or sets the external record identifier used for deduplication.
    /// </summary>
    public string? ExternalRecordId { get; set; }

    /// <summary>
    /// Gets or sets the primary timestamp for the usage record in UTC.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    /// Gets or sets the period start in UTC for aggregated records.
    /// </summary>
    public DateTimeOffset? PeriodStartUtc { get; set; }

    /// <summary>
    /// Gets or sets the period end in UTC for aggregated records.
    /// </summary>
    public DateTimeOffset? PeriodEndUtc { get; set; }

    /// <summary>
    /// Gets or sets the external user identifier.
    /// </summary>
    public string? UserIdentifier { get; set; }

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the AI provider.
    /// </summary>
    public AIProvider? Provider { get; set; }

    /// <summary>
    /// Gets or sets input token count.
    /// </summary>
    public long? InputTokens { get; set; }

    /// <summary>
    /// Gets or sets output token count.
    /// </summary>
    public long? OutputTokens { get; set; }

    /// <summary>
    /// Gets or sets cached input token count (cache read).
    /// </summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>
    /// Gets or sets cache-write token count (new prompt/context written into the provider cache).
    /// </summary>
    public long? CacheWriteTokens { get; set; }

    /// <summary>
    /// Gets or sets reasoning token count.
    /// </summary>
    public long? ReasoningTokens { get; set; }

    /// <summary>
    /// Gets or sets total token count.
    /// </summary>
    public long? TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the provider-reported cost.
    /// </summary>
    public decimal? ReportedCost { get; set; }

    /// <summary>
    /// Gets or sets the currency of the reported cost.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets the request count for aggregated records.
    /// </summary>
    public int? RequestCount { get; set; }

    /// <summary>
    /// Gets or sets optional metadata as JSON.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Gets or sets when the record was imported in UTC.
    /// </summary>
    public DateTimeOffset ImportedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the import batch identifier.
    /// </summary>
    public Guid? ImportBatchId { get; set; }

    /// <summary>
    /// Creates an external usage record.
    /// </summary>
    public static ExternalUsageRecord Create(
        UsageSource source,
        DateTimeOffset timestampUtc,
        string? externalRecordId = null,
        DateTimeOffset? periodStartUtc = null,
        DateTimeOffset? periodEndUtc = null,
        string? userIdentifier = null,
        string? model = null,
        AIProvider? provider = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? cachedInputTokens = null,
        long? cacheWriteTokens = null,
        long? reasoningTokens = null,
        long? totalTokens = null,
        decimal? reportedCost = null,
        string? currency = null,
        int? requestCount = null,
        string? metadataJson = null,
        Guid? importBatchId = null,
        DateTimeOffset? importedAtUtc = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        AgainstOptionalNegative(inputTokens);
        AgainstOptionalNegative(outputTokens);
        AgainstOptionalNegative(cachedInputTokens);
        AgainstOptionalNegative(cacheWriteTokens);
        AgainstOptionalNegative(reasoningTokens);
        AgainstOptionalNegative(totalTokens);
        if (reportedCost is not null)
        {
            Guard.AgainstNegative(reportedCost.Value);
        }

        if (requestCount is not null)
        {
            Guard.AgainstNegative(requestCount.Value);
        }

        var imported = importedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new ExternalUsageRecord(id ?? Guid.NewGuid(), createdAtUtc ?? imported)
        {
            Source = source,
            ExternalRecordId = externalRecordId,
            TimestampUtc = timestampUtc.ToUniversalTime(),
            PeriodStartUtc = periodStartUtc?.ToUniversalTime(),
            PeriodEndUtc = periodEndUtc?.ToUniversalTime(),
            UserIdentifier = userIdentifier,
            Model = model,
            Provider = provider,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CachedInputTokens = cachedInputTokens,
            CacheWriteTokens = cacheWriteTokens,
            ReasoningTokens = reasoningTokens,
            TotalTokens = totalTokens,
            ReportedCost = reportedCost,
            Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
            RequestCount = requestCount,
            MetadataJson = metadataJson,
            ImportedAtUtc = imported,
            ImportBatchId = importBatchId
        };
    }

    private static void AgainstOptionalNegative(long? value)
    {
        if (value is not null)
        {
            Guard.AgainstNegative(value.Value);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalUsageRecord"/> class.
    /// </summary>
    public ExternalUsageRecord()
    {
        TimestampUtc = DateTimeOffset.UtcNow;
        ImportedAtUtc = DateTimeOffset.UtcNow;
    }

    private ExternalUsageRecord(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
