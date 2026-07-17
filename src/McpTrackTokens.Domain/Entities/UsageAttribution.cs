using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// Attribution of an external usage record (or portion thereof) to a project or session.
/// </summary>
public sealed class UsageAttribution : EntityBase
{
    /// <summary>
    /// Gets or sets the external usage record identifier.
    /// </summary>
    public Guid ExternalUsageRecordId { get; set; }

    /// <summary>
    /// Gets or sets the attributed project identifier, when allocated.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the attributed editor session identifier, when known.
    /// </summary>
    public Guid? EditorSessionId { get; set; }

    /// <summary>
    /// Gets or sets the related activity event identifier, when known.
    /// </summary>
    public Guid? ActivityEventId { get; set; }

    /// <summary>
    /// Gets or sets the allocated cost portion.
    /// </summary>
    public decimal AllocatedCost { get; set; }

    /// <summary>
    /// Gets or sets the allocated input tokens.
    /// </summary>
    public long AllocatedInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the allocated output tokens.
    /// </summary>
    public long AllocatedOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the allocated total tokens.
    /// </summary>
    public long AllocatedTotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the allocation percentage (0–100).
    /// </summary>
    public decimal AllocationPercentage { get; set; }

    /// <summary>
    /// Gets or sets the attribution method.
    /// </summary>
    public AttributionMethod AttributionMethod { get; set; }

    /// <summary>
    /// Gets or sets the attribution confidence.
    /// </summary>
    public AttributionConfidence Confidence { get; set; }

    /// <summary>
    /// Gets or sets a human-readable reason for the attribution decision.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets when the attribution was reviewed in UTC.
    /// </summary>
    public DateTimeOffset? ReviewedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets who reviewed the attribution.
    /// </summary>
    public string? ReviewedBy { get; set; }

    /// <summary>
    /// Creates a usage attribution row.
    /// </summary>
    public static UsageAttribution Create(
        Guid externalUsageRecordId,
        AttributionMethod attributionMethod,
        AttributionConfidence confidence,
        decimal allocationPercentage,
        decimal allocatedCost = 0m,
        long allocatedInputTokens = 0,
        long allocatedOutputTokens = 0,
        long allocatedTotalTokens = 0,
        Guid? projectId = null,
        Guid? editorSessionId = null,
        Guid? activityEventId = null,
        string? reason = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        Guard.AgainstEmpty(externalUsageRecordId);
        Guard.AgainstOutOfRange(allocationPercentage, 0m, 100m);
        Guard.AgainstNegative(allocatedCost);
        Guard.AgainstNegative(allocatedInputTokens);
        Guard.AgainstNegative(allocatedOutputTokens);
        Guard.AgainstNegative(allocatedTotalTokens);

        return new UsageAttribution(id ?? Guid.NewGuid(), createdAtUtc ?? DateTimeOffset.UtcNow)
        {
            ExternalUsageRecordId = externalUsageRecordId,
            ProjectId = projectId,
            EditorSessionId = editorSessionId,
            ActivityEventId = activityEventId,
            AllocatedCost = allocatedCost,
            AllocatedInputTokens = allocatedInputTokens,
            AllocatedOutputTokens = allocatedOutputTokens,
            AllocatedTotalTokens = allocatedTotalTokens,
            AllocationPercentage = allocationPercentage,
            AttributionMethod = attributionMethod,
            Confidence = confidence,
            Reason = reason
        };
    }

    /// <summary>
    /// Marks the attribution as reviewed.
    /// </summary>
    public void MarkReviewed(string reviewedBy, DateTimeOffset? reviewedAtUtc = null)
    {
        ReviewedBy = Guard.AgainstNullOrWhiteSpace(reviewedBy).Trim();
        ReviewedAtUtc = reviewedAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageAttribution"/> class.
    /// </summary>
    public UsageAttribution()
    {
    }

    private UsageAttribution(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
