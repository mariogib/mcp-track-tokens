using FluentValidation;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Services;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Idempotent event ingestion with privacy defaults and project/session resolution.
/// </summary>
public sealed class EventIngestionService : IEventIngestionService
{
    private readonly IActivityEventRepository _events;
    private readonly ISessionRepository _sessions;
    private readonly IProjectRepository _projects;
    private readonly IProjectDetectionService _projectDetection;
    private readonly IActivityWindowService _activityWindows;
    private readonly IContentEncryptionService _encryption;
    private readonly ITimesheetManagementService _timesheets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<IngestEventDto> _validator;
    private readonly TrackingOptions _options;

    public EventIngestionService(
        IActivityEventRepository events,
        ISessionRepository sessions,
        IProjectRepository projects,
        IProjectDetectionService projectDetection,
        IActivityWindowService activityWindows,
        IContentEncryptionService encryption,
        ITimesheetManagementService timesheets,
        IUnitOfWork unitOfWork,
        IValidator<IngestEventDto> validator,
        IOptions<TrackingOptions> options)
    {
        _events = events;
        _sessions = sessions;
        _projects = projects;
        _projectDetection = projectDetection;
        _activityWindows = activityWindows;
        _encryption = encryption;
        _timesheets = timesheets;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IngestEventResultDto> IngestAsync(IngestEventDto dto, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken).ConfigureAwait(false);

        var editor = EnumParsing.ParseEditor(dto.Editor);
        var eventType = EnumParsing.ParseEventType(dto.EventType);
        if (!string.IsNullOrWhiteSpace(dto.ExternalEventId))
        {
            var existing = await _events
                .FindByExternalIdAsync(dto.ExternalEventId, editor, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (EnumParsing.IsTerminalAgentEvent(eventType))
                {
                    var refined = await TryCompletePromptSubmittedAsync(dto, editor, eventType, cancellationToken)
                        .ConfigureAwait(false);
                    if (refined)
                    {
                        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                return new IngestEventResultDto
                {
                    EventId = existing.Id,
                    WasDuplicate = true,
                    ProjectId = existing.ProjectId,
                    SessionId = existing.EditorSessionId
                };
            }
        }

        Project? project;
        var attributionMethod = AttributionMethod.Unallocated;
        var attributionConfidence = AttributionConfidence.Unallocated;

        if (dto.ProjectId is Guid explicitProjectId)
        {
            project = await _projects.GetByIdAsync(explicitProjectId, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), explicitProjectId);
            attributionMethod = AttributionMethod.ExplicitProject;
            attributionConfidence = AttributionConfidence.Certain;
        }
        else
        {
            project = await _projectDetection
                .DetectAsync(
                    dto.WorkspacePath,
                    dto.RepositoryPath,
                    dto.RemoteUrl,
                    dto.ActiveFilePath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (project is not null)
            {
                attributionMethod = AttributionMethod.RepositoryReported;
                attributionConfidence = AttributionConfidence.High;
            }
        }

        var session = await ResolveSessionAsync(dto, editor, project?.Id, eventType, cancellationToken)
            .ConfigureAwait(false);

        if (EnumParsing.IsTerminalAgentEvent(eventType))
        {
            await TryCompletePromptSubmittedAsync(dto, editor, eventType, cancellationToken)
                .ConfigureAwait(false);
        }

        string? promptHash = dto.PromptHash;
        string? encryptedContent = null;
        var storeContent = false;

        if (!string.IsNullOrEmpty(dto.PromptContent))
        {
            if (PromptPrivacy.ShouldHashPrompt(_options.EnablePromptHashing) &&
                session is not null &&
                string.IsNullOrEmpty(promptHash))
            {
                promptHash = PromptPrivacy.HashPrompt(session.Id, dto.PromptContent);
            }

            if (PromptPrivacy.ShouldStorePromptContent(_options.StorePromptContent))
            {
                if (!_encryption.IsConfigured)
                {
                    throw new InvalidOperationException(
                        "Prompt content storage is enabled but content encryption is not configured.");
                }

                encryptedContent = await _encryption
                    .EncryptAsync(dto.PromptContent, cancellationToken)
                    .ConfigureAwait(false);
                storeContent = true;
            }
        }

        var activityEvent = PromptActivityEvent.Create(
            eventType: eventType,
            editor: editor,
            timestampUtc: dto.TimestampUtc,
            projectId: project?.Id,
            editorSessionId: session?.Id,
            externalEventId: dto.ExternalEventId,
            externalConversationId: dto.ExternalConversationId,
            externalRequestId: dto.ExternalRequestId,
            workspacePath: dto.WorkspacePath,
            repositoryPath: dto.RepositoryPath,
            remoteUrl: dto.RemoteUrl,
            branch: dto.Branch,
            promptLength: dto.PromptLength ?? dto.PromptContent?.Length,
            promptHash: promptHash,
            promptContentStored: storeContent,
            promptContentEncrypted: encryptedContent,
            responseCompletedAtUtc: dto.ResponseCompletedAtUtc,
            durationMilliseconds: dto.DurationMilliseconds,
            model: dto.Model,
            provider: EnumParsing.ParseProvider(dto.Provider),
            status: ResolveStatus(dto, eventType),
            attributionMethod: attributionMethod,
            attributionConfidence: attributionConfidence,
            metadataJson: MetadataSerializer.Serialize(dto.Metadata, _options.MaxMetadataBytes));

        await _events.AddAsync(activityEvent, cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            Guid? assignProjectId = project is not null && session.ProjectId is null
                ? project.Id
                : null;
            await _sessions
                .TouchActivityAsync(session.Id, dto.TimestampUtc, assignProjectId, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _activityWindows.UpdateForEventAsync(activityEvent, cancellationToken).ConfigureAwait(false);

        return new IngestEventResultDto
        {
            EventId = activityEvent.Id,
            WasDuplicate = false,
            ProjectId = activityEvent.ProjectId,
            SessionId = activityEvent.EditorSessionId
        };
    }

    /// <inheritdoc />
    public async Task<BatchIngestResultDto> IngestBatchAsync(
        BatchIngestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<IngestEventResultDto>(request.Events.Count);
        var accepted = 0;
        var duplicates = 0;
        var failed = 0;

        foreach (var evt in request.Events)
        {
            try
            {
                var result = await IngestAsync(evt, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                if (result.WasDuplicate)
                {
                    duplicates++;
                }
                else
                {
                    accepted++;
                }
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return new BatchIngestResultDto
        {
            Accepted = accepted,
            Duplicates = duplicates,
            Failed = failed,
            Results = results
        };
    }

    /// <inheritdoc />
    public async Task<EditorSession> StartSessionAsync(SessionStartDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var editor = EnumParsing.ParseEditor(dto.Editor);
        Project? project = null;
        if (dto.ProjectId is Guid projectId)
        {
            project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), projectId);
        }
        else
        {
            project = await _projectDetection
                .DetectAsync(dto.WorkspacePath, dto.RepositoryPath, dto.RemoteUrl, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var activeForWorkspace = await _sessions
            .GetActiveForWorkspaceAsync(editor, dto.WorkspacePath, cancellationToken)
            .ConfigureAwait(false);
        if (activeForWorkspace is not null)
        {
            return await EnsureTrackedSessionAsync(activeForWorkspace, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(dto.ExternalSessionId))
        {
            var existing = await _sessions
                .GetByExternalSessionIdAsync(dto.ExternalSessionId, editor, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null && existing.Status == SessionStatus.Active)
            {
                return await EnsureTrackedSessionAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        var started = dto.StartedAtUtc ?? DateTimeOffset.UtcNow;
        await CloseOtherActiveSessionsAsync(keepSessionId: null, started, cancellationToken)
            .ConfigureAwait(false);

        var session = EditorSession.Start(
            editor,
            started,
            project?.Id,
            dto.EditorVersion,
            dto.MachineName,
            dto.UserName,
            dto.WorkspacePath,
            dto.RepositoryPath,
            dto.RemoteUrl,
            dto.Branch,
            dto.ExternalSessionId);

        await _sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);
        if (project?.Id is Guid pid)
        {
            await _timesheets.EnsureAutocreatedOpenEntryAsync(pid, started, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async Task<EditorSession?> EndSessionAsync(SessionEndDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var session = await FindSessionAsync(dto.SessionId, dto.ExternalSessionId, dto.Editor, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        session.TransitionTo(SessionStatus.Ended, dto.EndedAtUtc ?? DateTimeOffset.UtcNow);
        await _sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async Task<EditorSession?> HeartbeatAsync(HeartbeatDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var session = await FindSessionAsync(dto.SessionId, dto.ExternalSessionId, dto.Editor, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var at = dto.TimestampUtc ?? DateTimeOffset.UtcNow;
        session.RecordActivity(at);
        if (!string.IsNullOrWhiteSpace(dto.WorkspacePath))
        {
            session.WorkspacePath = dto.WorkspacePath;
        }

        if (!string.IsNullOrWhiteSpace(dto.RepositoryPath))
        {
            session.RepositoryPath = dto.RepositoryPath;
        }

        if (!string.IsNullOrWhiteSpace(dto.Branch))
        {
            session.Branch = dto.Branch;
        }

        await _sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<EditorSession?> ResolveSessionAsync(
        IngestEventDto dto,
        EditorType editor,
        Guid? projectId,
        ActivityEventType eventType,
        CancellationToken cancellationToken)
    {
        if (eventType == ActivityEventType.PromptSubmitted)
        {
            return await ResolveOrCreateSessionForPromptAsync(dto, editor, projectId, cancellationToken)
                .ConfigureAwait(false);
        }

        // Non-prompt events attach to an existing active workspace session only.
        return await _sessions
            .GetActiveForWorkspaceAsync(editor, dto.WorkspacePath, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<EditorSession> ResolveOrCreateSessionForPromptAsync(
        IngestEventDto dto,
        EditorType editor,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var active = await FindOpenSessionForPromptAsync(editor, dto.WorkspacePath, projectId, cancellationToken)
            .ConfigureAwait(false);

        if (active is not null)
        {
            var lastPromptAt = await _events
                .GetLatestPromptTimestampAsync(active.Id, cancellationToken)
                .ConfigureAwait(false) ?? active.LastActivityAtUtc;

            var inactivity = dto.TimestampUtc.ToUniversalTime() - lastPromptAt.ToUniversalTime();
            var closeAfter = TimeSpan.FromMinutes(Math.Max(1, _options.SessionInactivityCloseMinutes));

            if (inactivity > closeAfter)
            {
                var tracked = await EnsureTrackedSessionAsync(active, cancellationToken).ConfigureAwait(false);
                tracked.TransitionTo(SessionStatus.Ended, lastPromptAt);
                await _sessions.UpdateAsync(tracked, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var tracked = await EnsureTrackedSessionAsync(active, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(dto.ExternalSessionId) &&
                    !string.Equals(tracked.ExternalSessionId, dto.ExternalSessionId, StringComparison.Ordinal))
                {
                    tracked.ExternalSessionId = dto.ExternalSessionId;
                    await _sessions.UpdateAsync(tracked, cancellationToken).ConfigureAwait(false);
                }

                if (projectId is Guid ensureProjectId)
                {
                    await _timesheets.EnsureAutocreatedOpenEntryAsync(
                            ensureProjectId,
                            dto.TimestampUtc,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return tracked;
            }
        }

        await CloseOtherActiveSessionsAsync(keepSessionId: null, dto.TimestampUtc, cancellationToken)
            .ConfigureAwait(false);

        var created = EditorSession.Start(
            editor,
            dto.TimestampUtc,
            projectId,
            dto.EditorVersion,
            dto.MachineName,
            dto.UserName,
            dto.WorkspacePath,
            dto.RepositoryPath,
            dto.RemoteUrl,
            dto.Branch,
            dto.ExternalSessionId);
        await _sessions.AddAsync(created, cancellationToken).ConfigureAwait(false);
        if (projectId is Guid pid)
        {
            await _timesheets.EnsureAutocreatedOpenEntryAsync(pid, dto.TimestampUtc, cancellationToken)
                .ConfigureAwait(false);
        }

        return created;
    }

    private async Task<EditorSession?> FindOpenSessionForPromptAsync(
        EditorType editor,
        string? workspacePath,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var forWorkspace = await _sessions
            .GetActiveForWorkspaceAsync(editor, workspacePath, cancellationToken)
            .ConfigureAwait(false);
        if (forWorkspace is not null)
        {
            return forWorkspace;
        }

        if (projectId is null)
        {
            return null;
        }

        var active = await _sessions.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return active
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.LastActivityAtUtc)
            .FirstOrDefault();
    }

    private async Task CloseOtherActiveSessionsAsync(
        Guid? keepSessionId,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken)
    {
        var active = await _sessions.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var at = endedAtUtc.ToUniversalTime();
        foreach (var session in active)
        {
            if (keepSessionId is Guid keep && session.Id == keep)
            {
                continue;
            }

            var tracked = await _sessions.GetByIdAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (tracked is null || tracked.Status != SessionStatus.Active)
            {
                continue;
            }

            tracked.TransitionTo(SessionStatus.Ended, at);
            await _sessions.UpdateAsync(tracked, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<EditorSession> EnsureTrackedSessionAsync(
        EditorSession session,
        CancellationToken cancellationToken)
    {
        var tracked = await _sessions.GetByIdAsync(session.Id, cancellationToken).ConfigureAwait(false);
        return tracked ?? session;
    }

    private async Task<EditorSession?> FindSessionAsync(
        Guid? sessionId,
        string? externalSessionId,
        string? editor,
        CancellationToken cancellationToken)
    {
        if (sessionId is Guid id)
        {
            return await _sessions.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(externalSessionId))
        {
            return await _sessions
                .GetByExternalSessionIdAsync(externalSessionId, EnumParsing.ParseEditor(editor), cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static ActivityStatus ResolveStatus(IngestEventDto dto, ActivityEventType eventType)
    {
        var parsed = EnumParsing.ParseStatus(dto.Status);
        if (parsed != ActivityStatus.Unknown)
        {
            return parsed;
        }

        return EnumParsing.StatusFromEventType(eventType);
    }

    /// <summary>
    /// Updates the matching PromptSubmitted row with status and duration from a terminal agent event.
    /// </summary>
    private async Task<bool> TryCompletePromptSubmittedAsync(
        IngestEventDto dto,
        EditorType editor,
        ActivityEventType eventType,
        CancellationToken cancellationToken)
    {
        var generationKey = ResolveGenerationKey(dto);
        if (string.IsNullOrWhiteSpace(generationKey))
        {
            return false;
        }

        var prompt = await _events
            .FindByExternalIdAsync(generationKey, editor, cancellationToken)
            .ConfigureAwait(false);

        if (prompt is null || prompt.EventType != ActivityEventType.PromptSubmitted)
        {
            // Fallback: ExternalRequestId may differ from ExternalEventId on older rows.
            if (!string.IsNullOrWhiteSpace(dto.ExternalRequestId))
            {
                var byRequest = await _events
                    .FindByExternalRequestIdAsync(dto.ExternalRequestId, cancellationToken)
                    .ConfigureAwait(false);
                if (byRequest is { EventType: ActivityEventType.PromptSubmitted })
                {
                    prompt = await _events.GetByIdAsync(byRequest.Id, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (prompt is null || prompt.EventType != ActivityEventType.PromptSubmitted)
        {
            return false;
        }

        var completedAt = dto.ResponseCompletedAtUtc ?? dto.TimestampUtc;
        var status = ResolveStatus(dto, eventType);
        prompt.ApplyCompletion(status, completedAt, dto.DurationMilliseconds, dto.Model);
        await _events.UpdateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string? ResolveGenerationKey(IngestEventDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.ExternalRequestId))
        {
            return dto.ExternalRequestId.Trim();
        }

        if (string.IsNullOrWhiteSpace(dto.ExternalEventId))
        {
            return null;
        }

        var id = dto.ExternalEventId.Trim();
        var separators = new[]
        {
            ":AgentCompleted",
            ":AgentFailed",
            ":AgentCancelled"
        };
        foreach (var suffix in separators)
        {
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return id[..^suffix.Length];
            }
        }

        return id;
    }
}
