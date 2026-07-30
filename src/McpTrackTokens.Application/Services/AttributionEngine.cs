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
/// prior prompt that uses the same model (second precision). When no exact
/// model match exists, falls back to the closest prior prompt with model Auto.
/// Prompts are never consumed: several usage rows may share one prompt.
/// </summary>
public sealed class AttributionEngine : IAttributionEngine
{
    internal const string AutoModelFallback = "Auto";

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

        var usageSecond = TimestampPrecision.RoundToSecond(usageRecord.TimestampUtc);
        var usageModelLabel = string.IsNullOrWhiteSpace(usageRecord.Model)
            ? "(no model)"
            : usageRecord.Model.Trim();

        var prompt = await _events
            .FindClosestPriorPromptWithProjectAsync(
                usageRecord.TimestampUtc,
                usageRecord.Model,
                cancellationToken)
            .ConfigureAwait(false);

        if (prompt?.ProjectId is Guid projectId)
        {
            return [CreateLinked(
                usageRecord,
                prompt,
                projectId,
                AttributionConfidence.High,
                exactModelMatch: true,
                usageModelLabel,
                usageSecond)];
        }

        // Cursor often records prompts as Auto while the usage export names the
        // resolved model — fall back to closest prior Auto prompt by timestamp.
        if (!IsAutoModel(usageRecord.Model))
        {
            var autoPrompt = await _events
                .FindClosestPriorPromptWithProjectAsync(
                    usageRecord.TimestampUtc,
                    AutoModelFallback,
                    cancellationToken)
                .ConfigureAwait(false);

            if (autoPrompt?.ProjectId is Guid autoProjectId)
            {
                return [CreateLinked(
                    usageRecord,
                    autoPrompt,
                    autoProjectId,
                    AttributionConfidence.Medium,
                    exactModelMatch: false,
                    usageModelLabel,
                    usageSecond)];
            }
        }

        return [CreateSingle(
            usageRecord,
            projectId: null,
            editorSessionId: null,
            activityEventId: null,
            AttributionMethod.Unallocated,
            AttributionConfidence.Unallocated,
            $"No prompt with a project and matching model '{usageModelLabel}' (or Auto fallback) found at or before this usage timestamp (second precision).")];
    }

    private static UsageAttribution CreateLinked(
        ExternalUsageRecord usageRecord,
        PromptActivityEvent prompt,
        Guid projectId,
        AttributionConfidence confidence,
        bool exactModelMatch,
        string usageModelLabel,
        DateTimeOffset usageSecond)
    {
        var promptSecond = TimestampPrecision.RoundToSecond(prompt.TimestampUtc);
        var deltaSeconds = (usageSecond - promptSecond).TotalSeconds;
        var reason = exactModelMatch
            ? $"Linked to closest prior prompt {prompt.Id:D} with matching model '{usageModelLabel}' at {promptSecond:yyyy-MM-dd HH:mm:ss}Z (usage {usageSecond:yyyy-MM-dd HH:mm:ss}Z, Δ {deltaSeconds:0} s); project from that prompt."
            : $"No prompt with model '{usageModelLabel}'; linked to closest prior Auto prompt {prompt.Id:D} at {promptSecond:yyyy-MM-dd HH:mm:ss}Z (usage {usageSecond:yyyy-MM-dd HH:mm:ss}Z, Δ {deltaSeconds:0} s); project from that prompt.";

        return CreateSingle(
            usageRecord,
            projectId,
            prompt.EditorSessionId,
            prompt.Id,
            AttributionMethod.ClosestPromptMatch,
            confidence,
            reason);
    }

    private static bool IsAutoModel(string? model)
    {
        var normalized = CursorTokenCostCalculator.NormalizeModelName(model);
        return string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase);
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
        => CursorTokenCostCalculator.ResolveTotalTokens(usage);

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
