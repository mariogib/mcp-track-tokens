using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Dashboard admin create / update / delete for editor sessions.
/// </summary>
public sealed class SessionManagementService : ISessionManagementService
{
    private readonly IProjectRepository _projects;
    private readonly ISessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateProjectSessionRequest> _createValidator;
    private readonly IValidator<UpdateSessionRequest> _updateValidator;

    public SessionManagementService(
        IProjectRepository projects,
        ISessionRepository sessions,
        IUnitOfWork unitOfWork,
        IValidator<CreateProjectSessionRequest> createValidator,
        IValidator<UpdateSessionRequest> updateValidator)
    {
        _projects = projects;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <inheritdoc />
    public async Task<EditorSession> CreateForProjectAsync(
        Guid projectId,
        CreateProjectSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var started = (request.StartedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var status = ParseStatus(request.Status, request.EndedAtUtc.HasValue ? SessionStatus.Ended : SessionStatus.Active);
        var editor = EnumParsing.ParseEditor(request.Editor);

        var session = EditorSession.Start(
            editor,
            started,
            projectId,
            request.EditorVersion,
            request.MachineName,
            request.UserName,
            request.WorkspacePath,
            request.RepositoryPath,
            request.RemoteUrl,
            request.Branch,
            request.ExternalSessionId);

        session.ApplyAdminEdit(
            projectId,
            editor,
            request.EditorVersion,
            request.MachineName,
            request.UserName,
            request.WorkspacePath,
            request.RepositoryPath,
            request.RemoteUrl,
            request.Branch,
            request.ExternalSessionId,
            started,
            request.EndedAtUtc,
            status);

        await _sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async Task<EditorSession> UpdateAsync(
        Guid sessionId,
        UpdateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var session = await _sessions.GetByIdAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(EditorSession), sessionId);

        Guid? projectId = request.ProjectId ?? session.ProjectId;
        if (projectId is Guid pid)
        {
            _ = await _projects.GetByIdAsync(pid, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Project), pid);
        }

        var status = ParseStatus(request.Status, session.Status);
        session.ApplyAdminEdit(
            projectId,
            EnumParsing.ParseEditor(request.Editor),
            request.EditorVersion,
            request.MachineName,
            request.UserName,
            request.WorkspacePath,
            request.RepositoryPath,
            request.RemoteUrl,
            request.Branch,
            request.ExternalSessionId,
            request.StartedAtUtc,
            request.EndedAtUtc,
            status);

        await _sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(EditorSession), sessionId);

        await _sessions.DeleteAsync(session, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SessionStatus ParseStatus(string? value, SessionStatus fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<SessionStatus>(value.Trim(), ignoreCase: true, out var status))
        {
            return status;
        }

        throw new Domain.Exceptions.ValidationException(
            nameof(UpdateSessionRequest.Status),
            $"Unsupported session status '{value}'.");
    }
}
