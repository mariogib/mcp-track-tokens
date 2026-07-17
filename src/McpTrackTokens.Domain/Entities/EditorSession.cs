using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Validation;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A tracked editor session bound to a project and workspace context.
/// </summary>
public sealed class EditorSession : EntityBase, IAuditable
{
    /// <summary>
    /// Gets or sets the associated project identifier, when known.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the editor type.
    /// </summary>
    public EditorType Editor { get; set; }

    /// <summary>
    /// Gets or sets the editor version string.
    /// </summary>
    public string? EditorVersion { get; set; }

    /// <summary>
    /// Gets or sets the machine name.
    /// </summary>
    public string? MachineName { get; set; }

    /// <summary>
    /// Gets or sets the OS user name.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the workspace path.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Gets or sets the repository path.
    /// </summary>
    public string? RepositoryPath { get; set; }

    /// <summary>
    /// Gets or sets the remote Git URL.
    /// </summary>
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Gets or sets the current Git branch.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Gets or sets the external editor session identifier.
    /// </summary>
    public string? ExternalSessionId { get; set; }

    /// <summary>
    /// Gets or sets when the session started (UTC).
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the session ended (UTC), if ended.
    /// </summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the last observed activity timestamp (UTC).
    /// </summary>
    public DateTimeOffset LastActivityAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the session status.
    /// </summary>
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Creates a new active editor session.
    /// </summary>
    public static EditorSession Start(
        EditorType editor,
        DateTimeOffset startedAtUtc,
        Guid? projectId = null,
        string? editorVersion = null,
        string? machineName = null,
        string? userName = null,
        string? workspacePath = null,
        string? repositoryPath = null,
        string? remoteUrl = null,
        string? branch = null,
        string? externalSessionId = null,
        Guid? id = null)
    {
        var started = startedAtUtc.ToUniversalTime();
        return new EditorSession(id ?? Guid.NewGuid(), started)
        {
            ProjectId = projectId,
            Editor = editor,
            EditorVersion = editorVersion,
            MachineName = machineName,
            UserName = userName,
            WorkspacePath = workspacePath,
            RepositoryPath = repositoryPath,
            RemoteUrl = remoteUrl,
            Branch = branch,
            ExternalSessionId = externalSessionId,
            StartedAtUtc = started,
            LastActivityAtUtc = started,
            Status = SessionStatus.Active,
            UpdatedAtUtc = started
        };
    }

    /// <summary>
    /// Transitions the session to a new status when allowed.
    /// </summary>
    public void TransitionTo(SessionStatus newStatus, DateTimeOffset? atUtc = null)
    {
        SessionTransitionValidator.EnsureCanTransition(Status, newStatus);
        var when = atUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        Status = newStatus;
        UpdatedAtUtc = when;

        if (newStatus is SessionStatus.Ended or SessionStatus.Abandoned)
        {
            EndedAtUtc ??= when;
        }

        if (newStatus == SessionStatus.Active)
        {
            EndedAtUtc = null;
        }
    }

    /// <summary>
    /// Records activity against the session.
    /// </summary>
    public void RecordActivity(DateTimeOffset activityAtUtc)
    {
        var when = activityAtUtc.ToUniversalTime();
        if (when > LastActivityAtUtc)
        {
            LastActivityAtUtc = when;
        }

        UpdatedAtUtc = when;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorSession"/> class.
    /// </summary>
    public EditorSession()
    {
        var now = DateTimeOffset.UtcNow;
        StartedAtUtc = now;
        LastActivityAtUtc = now;
        UpdatedAtUtc = now;
    }

    private EditorSession(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UpdatedAtUtc = createdAtUtc;
    }
}
