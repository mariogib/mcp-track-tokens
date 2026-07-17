using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// A Git repository associated with a project.
/// </summary>
public sealed class ProjectRepository : EntityBase
{
    /// <summary>
    /// Gets or sets the owning project identifier.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the local filesystem path.
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized local path used for matching.
    /// </summary>
    public string NormalizedPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the remote Git URL.
    /// </summary>
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Gets or sets the normalized remote URL used for matching.
    /// </summary>
    public string? NormalizedRemoteUrl { get; set; }

    /// <summary>
    /// Gets or sets the default branch name.
    /// </summary>
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Gets or sets whether this repository mapping is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Creates a repository mapping with normalized path and remote URL values.
    /// </summary>
    public static ProjectRepository Create(
        Guid projectId,
        string localPath,
        string? remoteUrl = null,
        string? defaultBranch = null,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        Guard.AgainstEmpty(projectId);
        var path = Guard.AgainstNullOrWhiteSpace(localPath);

        var entity = new ProjectRepository(id ?? Guid.NewGuid(), createdAtUtc ?? DateTimeOffset.UtcNow)
        {
            ProjectId = projectId,
            LocalPath = path.Trim(),
            NormalizedPath = ValueObjects.NormalizedPath.Normalize(path),
            RemoteUrl = string.IsNullOrWhiteSpace(remoteUrl) ? null : remoteUrl.Trim(),
            NormalizedRemoteUrl = string.IsNullOrWhiteSpace(remoteUrl)
                ? null
                : ValueObjects.NormalizedRemoteUrl.Normalize(remoteUrl),
            DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? null : defaultBranch.Trim(),
            IsActive = true
        };

        return entity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectRepository"/> class.
    /// </summary>
    public ProjectRepository()
    {
    }

    private ProjectRepository(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
