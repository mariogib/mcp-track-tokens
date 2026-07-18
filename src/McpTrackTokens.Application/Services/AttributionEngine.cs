using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Services;
namespace McpTrackTokens.Application.Services;

/// <summary>
/// Attributes imported usage to a project by linking each row to the closest
/// prompt at or before the usage timestamp (second precision).
/// Prompts are never consumed: several usage rows may share one prompt.
/// </summary>
public sealed class AttributionEngine : IAttributionEngine
{
    private readonly IActivityEventRepository _events;
    private readonly IUsageAttributionRepository _attributions;
    private readonly IExternalUsageRepository _usage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<AllocationRequestDto> _allocationValidator;
    private readonly CostAllocationCalculator _costAllocator = new();

    public AttributionEngine(
        IActivityEventRepository events,
        IUsageAttributionRepository attributions,
        IExternalUsageRepository usage,
        IUnitOfWork unitOfWork,
        IValidator<AllocationRequestDto> allocationValidator)
    {
        _events = events;
        _attributions = attributions;
        _usage = usage;
        _unitOfWork = unitOfWork;
        _allocationValidator = allocationValidator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> ProposeAsync(
        ExternalUsageRecord usageRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usageRecord);

        var prompt = await _events
            .FindClosestPriorPromptWithProjectAsync(usageRecord.TimestampUtc, cancellationToken)
            .ConfigureAwait(false);

        if (prompt?.ProjectId is Guid projectId)
        {
            var usageSecond = TimestampPrecision.RoundToSecond(usageRecord.TimestampUtc);
            var promptSecond = TimestampPrecision.RoundToSecond(prompt.TimestampUtc);
            var deltaSeconds = (usageSecond - promptSecond).TotalSeconds;
            return [CreateSingle(
                usageRecord,
                projectId,
                prompt.EditorSessionId,
                prompt.Id,
                AttributionMethod.ClosestPromptMatch,
                AttributionConfidence.High,
                $"Linked to closest prior prompt {prompt.Id:D} at {promptSecond:yyyy-MM-dd HH:mm:ss}Z (usage {usageSecond:yyyy-MM-dd HH:mm:ss}Z, Δ {deltaSeconds:0} s); project from that prompt.")];
        }

        return [CreateSingle(
            usageRecord,
            projectId: null,
            editorSessionId: null,
            activityEventId: null,
            AttributionMethod.Unallocated,
            AttributionConfidence.Unallocated,
            "No prompt with a project found at or before this usage timestamp (second precision).")];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> AttributeAsync(
        ExternalUsageRecord usageRecord,
        CancellationToken cancellationToken = default)
    {
        var proposed = await ProposeAsync(usageRecord, cancellationToken).ConfigureAwait(false);
        await PersistAsync(usageRecord.Id, proposed, cancellationToken).ConfigureAwait(false);
        return proposed;
    }

    /// <inheritdoc />
    public async Task PersistAsync(
        Guid externalUsageRecordId,
        IReadOnlyList<UsageAttribution> attributions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributions);
        await _attributions.DeleteForUsageRecordAsync(externalUsageRecordId, cancellationToken).ConfigureAwait(false);
        if (attributions.Count > 0)
        {
            await _attributions.AddRangeAsync(attributions, cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> AttributeManualAsync(
        AllocationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await _allocationValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var usage = await _usage.GetByIdAsync(request.UsageRecordId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(ExternalUsageRecord), request.UsageRecordId);

        if (request.ReplaceExisting)
        {
            await _attributions.DeleteForUsageRecordAsync(usage.Id, cancellationToken).ConfigureAwait(false);
        }

        var totalCost = usage.ReportedCost ?? 0m;
        var targets = request.ProjectAllocations
            .Select(p => (Key: p.ProjectId.ToString("D"), Percentage: p.Percentage))
            .ToArray();
        var shares = _costAllocator.AllocateByPercentages(totalCost, targets);
        var totalTokens = ResolveTotalTokens(usage);

        var results = new List<UsageAttribution>(shares.Count);
        for (var i = 0; i < shares.Count; i++)
        {
            var share = shares[i];
            var allocation = request.ProjectAllocations[i];
            var projectId = Guid.Parse(share.Key);
            var ratio = share.Percentage.ToRatio();
            var attribution = UsageAttribution.Create(
                usage.Id,
                AttributionMethod.Manual,
                AttributionConfidence.Certain,
                share.Percentage.Value,
                share.Amount,
                allocatedInputTokens: (long)Math.Round((usage.InputTokens ?? 0) * ratio, MidpointRounding.AwayFromZero),
                allocatedOutputTokens: (long)Math.Round((usage.OutputTokens ?? 0) * ratio, MidpointRounding.AwayFromZero),
                allocatedTotalTokens: (long)Math.Round(totalTokens * ratio, MidpointRounding.AwayFromZero),
                projectId: projectId,
                editorSessionId: allocation.EditorSessionId,
                activityEventId: allocation.ActivityEventId,
                reason: request.Reason ?? "Manual allocation.");

            if (!string.IsNullOrWhiteSpace(request.ReviewedBy))
            {
                attribution.MarkReviewed(request.ReviewedBy);
            }

            results.Add(attribution);
        }

        await _attributions.AddRangeAsync(results, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Resolves total tokens for eligibility and allocation (prefers reported total, else sum of parts).
    /// </summary>
    internal static long ResolveTotalTokens(ExternalUsageRecord usage)
    {
        if (usage.TotalTokens is > 0)
        {
            return usage.TotalTokens.Value;
        }

        var derived = (usage.InputTokens ?? 0)
            + (usage.OutputTokens ?? 0)
            + (usage.CachedInputTokens ?? 0)
            + (usage.ReasoningTokens ?? 0);
        return usage.TotalTokens ?? derived;
    }

    private static UsageAttribution CreateSingle(
        ExternalUsageRecord usage,
        Guid? projectId,
        Guid? editorSessionId,
        Guid? activityEventId,
        AttributionMethod method,
        AttributionConfidence confidence,
        string reason)
    {
        var percentage = projectId is null ? 0m : 100m;
        var cost = projectId is null ? 0m : usage.ReportedCost ?? 0m;
        var totalTokens = projectId is null ? 0L : ResolveTotalTokens(usage);
        return UsageAttribution.Create(
            usage.Id,
            method,
            confidence,
            percentage,
            cost,
            allocatedInputTokens: projectId is null ? 0 : usage.InputTokens ?? 0,
            allocatedOutputTokens: projectId is null ? 0 : usage.OutputTokens ?? 0,
            allocatedTotalTokens: totalTokens,
            projectId: projectId,
            editorSessionId: editorSessionId,
            activityEventId: activityEventId,
            reason: reason);
    }
}
