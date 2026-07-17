using System.Text.Json;
using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Services;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Deterministic usage attribution with ordered matching strategies.
/// Never silently promotes Low confidence to Certain.
/// </summary>
public sealed class AttributionEngine : IAttributionEngine
{
    private readonly IProjectRepository _projects;
    private readonly ISessionRepository _sessions;
    private readonly IActivityEventRepository _events;
    private readonly IActivityWindowRepository _windows;
    private readonly IUsageAttributionRepository _attributions;
    private readonly IExternalUsageRepository _usage;
    private readonly IPathNormalizer _pathNormalizer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<AllocationRequestDto> _allocationValidator;
    private readonly CostAllocationCalculator _costAllocator = new();

    public AttributionEngine(
        IProjectRepository projects,
        ISessionRepository sessions,
        IActivityEventRepository events,
        IActivityWindowRepository windows,
        IUsageAttributionRepository attributions,
        IExternalUsageRepository usage,
        IPathNormalizer pathNormalizer,
        IUnitOfWork unitOfWork,
        IValidator<AllocationRequestDto> allocationValidator)
    {
        _projects = projects;
        _sessions = sessions;
        _events = events;
        _windows = windows;
        _attributions = attributions;
        _usage = usage;
        _pathNormalizer = pathNormalizer;
        _unitOfWork = unitOfWork;
        _allocationValidator = allocationValidator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsageAttribution>> ProposeAsync(
        ExternalUsageRecord usageRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usageRecord);
        var context = ParseContext(usageRecord);

        if (!string.IsNullOrWhiteSpace(context.RepositoryPath) || !string.IsNullOrWhiteSpace(context.RemoteUrl))
        {
            Project? project = null;
            if (!string.IsNullOrWhiteSpace(context.RepositoryPath))
            {
                project = await _projects
                    .FindByNormalizedPathAsync(_pathNormalizer.Normalize(context.RepositoryPath), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (project is null && !string.IsNullOrWhiteSpace(context.RemoteUrl))
            {
                project = await _projects
                    .FindByNormalizedRemoteUrlAsync(_pathNormalizer.NormalizeRemoteUrl(context.RemoteUrl), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (project is not null)
            {
                return [CreateSingle(
                    usageRecord,
                    project.Id,
                    null,
                    null,
                    AttributionMethod.RepositoryReported,
                    AttributionConfidence.Certain,
                    "Matched imported repository path or remote URL.")];
            }
        }

        if (context.ExplicitProjectId is Guid explicitId)
        {
            var project = await _projects.GetByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
            if (project is not null)
            {
                return [CreateSingle(
                    usageRecord,
                    project.Id,
                    null,
                    null,
                    AttributionMethod.ExplicitProject,
                    AttributionConfidence.Certain,
                    "Explicit project identifier present on usage record.")];
            }
        }

        if (!string.IsNullOrWhiteSpace(context.ExternalSessionId))
        {
            var session = await _sessions
                .GetByExternalSessionIdAsync(context.ExternalSessionId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (session?.ProjectId is Guid sessionProjectId)
            {
                return [CreateSingle(
                    usageRecord,
                    sessionProjectId,
                    session.Id,
                    null,
                    AttributionMethod.ExternalSessionMatch,
                    AttributionConfidence.High,
                    "Matched external editor session identifier.")];
            }
        }

        if (!string.IsNullOrWhiteSpace(context.ExternalRequestId))
        {
            var byRequest = await _events
                .FindByExternalRequestIdAsync(context.ExternalRequestId, cancellationToken)
                .ConfigureAwait(false);
            if (byRequest?.ProjectId is Guid requestProjectId)
            {
                return [CreateSingle(
                    usageRecord,
                    requestProjectId,
                    byRequest.EditorSessionId,
                    byRequest.Id,
                    AttributionMethod.ExternalSessionMatch,
                    AttributionConfidence.High,
                    "Matched external request identifier to an activity event.")];
            }
        }

        if (!string.IsNullOrWhiteSpace(context.ExternalConversationId))
        {
            var byConversation = await _events
                .FindByExternalConversationIdAsync(context.ExternalConversationId, cancellationToken)
                .ConfigureAwait(false);
            if (byConversation?.ProjectId is Guid conversationProjectId)
            {
                return [CreateSingle(
                    usageRecord,
                    conversationProjectId,
                    byConversation.EditorSessionId,
                    byConversation.Id,
                    AttributionMethod.ExternalSessionMatch,
                    AttributionConfidence.High,
                    "Matched external conversation identifier to an activity event.")];
            }
        }

        var activeSessions = await _sessions
            .GetActiveAtAsync(usageRecord.TimestampUtc, cancellationToken)
            .ConfigureAwait(false);
        var withProject = activeSessions.Where(s => s.ProjectId is not null).ToList();
        if (withProject.Count == 1)
        {
            var only = withProject[0];
            return [CreateSingle(
                usageRecord,
                only.ProjectId!.Value,
                only.Id,
                null,
                AttributionMethod.SingleActiveSession,
                AttributionConfidence.High,
                "Only one active project session existed at the usage timestamp.")];
        }

        var windows = await _windows
            .ListAsync(
                usageRecord.TimestampUtc.AddMinutes(-1),
                usageRecord.TimestampUtc.AddMinutes(1),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var covering = windows
            .Where(w =>
                w.ProjectId is not null &&
                w.StartedAtUtc <= usageRecord.TimestampUtc &&
                w.EndedAtUtc >= usageRecord.TimestampUtc)
            .ToList();

        var distinctProjects = covering.Select(w => w.ProjectId!.Value).Distinct().ToList();
        if (distinctProjects.Count == 1)
        {
            var window = covering[0];
            return [CreateSingle(
                usageRecord,
                distinctProjects[0],
                window.EditorSessionId,
                null,
                AttributionMethod.TimeWindowMatch,
                AttributionConfidence.Medium,
                "Usage timestamp fell inside a single project activity window.")];
        }

        if (distinctProjects.Count > 1)
        {
            var weights = distinctProjects
                .Select(projectId =>
                {
                    var seconds = covering
                        .Where(w => w.ProjectId == projectId)
                        .Sum(w => w.DurationSeconds);
                    return new AllocationWeight(projectId.ToString("D"), seconds);
                })
                .ToArray();

            var totalCost = usageRecord.ReportedCost ?? 0m;
            var shares = _costAllocator.AllocateProportionally(totalCost, weights);
            return shares
                .Select(share => CreateFromShare(
                    usageRecord,
                    Guid.Parse(share.Key),
                    share,
                    AttributionMethod.ProportionalTimeAllocation,
                    AttributionConfidence.Low,
                    "Allocated proportionally across overlapping activity windows."))
                .ToList();
        }

        return [CreateSingle(
            usageRecord,
            projectId: null,
            editorSessionId: null,
            activityEventId: null,
            AttributionMethod.Unallocated,
            AttributionConfidence.Unallocated,
            "No deterministic attribution rule matched.")];
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

        var results = new List<UsageAttribution>(shares.Count);
        for (var i = 0; i < shares.Count; i++)
        {
            var share = shares[i];
            var allocation = request.ProjectAllocations[i];
            var projectId = Guid.Parse(share.Key);
            var attribution = CreateFromShare(
                usage,
                projectId,
                share,
                AttributionMethod.Manual,
                AttributionConfidence.Certain,
                request.Reason ?? "Manual allocation.",
                allocation.EditorSessionId,
                allocation.ActivityEventId);

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

    private static UsageAttribution CreateSingle(
        ExternalUsageRecord usage,
        Guid? projectId,
        Guid? editorSessionId,
        Guid? activityEventId,
        AttributionMethod method,
        AttributionConfidence confidence,
        string reason)
    {
        confidence = NormalizeConfidence(method, confidence);
        var percentage = projectId is null ? 0m : 100m;
        var cost = projectId is null ? 0m : usage.ReportedCost ?? 0m;
        var share = new AllocationShare(
            projectId?.ToString("D") ?? "unallocated",
            new Percentage(percentage),
            cost);
        return CreateFromShare(usage, projectId, share, method, confidence, reason, editorSessionId, activityEventId);
    }

    private static UsageAttribution CreateFromShare(
        ExternalUsageRecord usage,
        Guid? projectId,
        AllocationShare share,
        AttributionMethod method,
        AttributionConfidence confidence,
        string reason,
        Guid? editorSessionId = null,
        Guid? activityEventId = null)
    {
        confidence = NormalizeConfidence(method, confidence);
        var ratio = share.Percentage.ToRatio();
        return UsageAttribution.Create(
            usage.Id,
            method,
            confidence,
            share.Percentage.Value,
            share.Amount,
            allocatedInputTokens: (long)Math.Round((usage.InputTokens ?? 0) * ratio, MidpointRounding.AwayFromZero),
            allocatedOutputTokens: (long)Math.Round((usage.OutputTokens ?? 0) * ratio, MidpointRounding.AwayFromZero),
            allocatedTotalTokens: (long)Math.Round((usage.TotalTokens ?? 0) * ratio, MidpointRounding.AwayFromZero),
            projectId: projectId,
            editorSessionId: editorSessionId,
            activityEventId: activityEventId,
            reason: reason);
    }

    private static AttributionConfidence NormalizeConfidence(
        AttributionMethod method,
        AttributionConfidence confidence)
    {
        if (confidence == AttributionConfidence.Certain &&
            method is AttributionMethod.ProportionalTimeAllocation or AttributionMethod.TimeWindowMatch)
        {
            return method == AttributionMethod.TimeWindowMatch
                ? AttributionConfidence.Medium
                : AttributionConfidence.Low;
        }

        if (confidence == AttributionConfidence.Certain && method == AttributionMethod.SingleActiveSession)
        {
            return AttributionConfidence.High;
        }

        return confidence;
    }

    private static UsageContext ParseContext(ExternalUsageRecord usage)
    {
        if (string.IsNullOrWhiteSpace(usage.MetadataJson))
        {
            return new UsageContext(null, null, null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(usage.MetadataJson);
            var root = doc.RootElement;
            return new UsageContext(
                GetString(root, "repositoryPath") ?? GetString(root, "RepositoryPath"),
                GetString(root, "remoteUrl") ?? GetString(root, "RemoteUrl"),
                GetString(root, "externalSessionId") ?? GetString(root, "ExternalSessionId"),
                GetString(root, "externalRequestId") ?? GetString(root, "ExternalRequestId"),
                GetString(root, "externalConversationId") ?? GetString(root, "ExternalConversationId"),
                GetGuid(root, "projectId") ?? GetGuid(root, "ProjectId") ?? GetGuid(root, "explicitProjectId"));
        }
        catch (JsonException)
        {
            return new UsageContext(null, null, null, null, null, null);
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? GetGuid(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record UsageContext(
        string? RepositoryPath,
        string? RemoteUrl,
        string? ExternalSessionId,
        string? ExternalRequestId,
        string? ExternalConversationId,
        Guid? ExplicitProjectId);
}
