namespace McpTrackTokens.Application.DTOs;

/// <summary>
/// Request to create a project session from the dashboard (admin CRUD).
/// </summary>
public sealed record CreateProjectSessionRequest
{
    public string Editor { get; init; } = "Cursor";

    public string? EditorVersion { get; init; }

    public string? MachineName { get; init; }

    public string? UserName { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? Branch { get; init; }

    public string? ExternalSessionId { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    /// <summary>
    /// Session status name (<c>Active</c>, <c>Paused</c>, <c>Ended</c>, <c>Abandoned</c>).
    /// </summary>
    public string? Status { get; init; }
}

/// <summary>
/// Request to update an existing session from the dashboard (admin CRUD).
/// </summary>
public sealed record UpdateSessionRequest
{
    public Guid? ProjectId { get; init; }

    public string Editor { get; init; } = "Cursor";

    public string? EditorVersion { get; init; }

    public string? MachineName { get; init; }

    public string? UserName { get; init; }

    public string? WorkspacePath { get; init; }

    public string? RepositoryPath { get; init; }

    public string? RemoteUrl { get; init; }

    public string? Branch { get; init; }

    public string? ExternalSessionId { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    /// <summary>
    /// Session status name (<c>Active</c>, <c>Paused</c>, <c>Ended</c>, <c>Abandoned</c>).
    /// </summary>
    public string Status { get; init; } = "Active";
}
