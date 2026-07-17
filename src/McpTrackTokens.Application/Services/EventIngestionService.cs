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
        _unitOfWork = unitOfWork;
        _validator = validator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IngestEventResultDto> IngestAsync(IngestEventDto dto, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken).ConfigureAwait(false);

        var editor = EnumParsing.ParseEditor(dto.Editor);
        if (!string.IsNullOrWhiteSpace(dto.ExternalEventId))
        {
            var existing = await _events
                .FindByExternalIdAsync(dto.ExternalEventId, editor, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
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

        var session = await ResolveSessionAsync(dto, editor, project?.Id, cancellationToken).ConfigureAwait(false);

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
            eventType: EnumParsing.ParseEventType(dto.EventType),
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
            status: EnumParsing.ParseStatus(dto.Status),
            attributionMethod: attributionMethod,
            attributionConfidence: attributionConfidence,
            metadataJson: MetadataSerializer.Serialize(dto.Metadata, _options.MaxMetadataBytes));

        await _events.AddAsync(activityEvent, cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            session.RecordActivity(dto.TimestampUtc);
            if (project is not null && session.ProjectId is null)
            {
                session.ProjectId = project.Id;
            }

            await _sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(dto.ExternalSessionId))
        {
            var existing = await _sessions
                .GetByExternalSessionIdAsync(dto.ExternalSessionId, editor, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null && existing.Status == SessionStatus.Active)
            {
                return existing;
            }
        }

        var session = EditorSession.Start(
            editor,
            dto.StartedAtUtc ?? DateTimeOffset.UtcNow,
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.ExternalSessionId))
        {
            return null;
        }

        var existing = await _sessions
            .GetByExternalSessionIdAsync(dto.ExternalSessionId, editor, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

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
        return created;
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
}
