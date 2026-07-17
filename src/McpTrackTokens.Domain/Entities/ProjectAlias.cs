using McpTrackTokens.Domain.Common;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.ValueObjects;

namespace McpTrackTokens.Domain.Entities;

/// <summary>
/// An alternate identifier that maps external names or paths to a project.
/// </summary>
public sealed class ProjectAlias : EntityBase
{
    /// <summary>
    /// Gets or sets the owning project identifier.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the raw alias value.
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized alias used for matching.
    /// </summary>
    public string NormalizedAlias { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the alias type.
    /// </summary>
    public AliasType AliasType { get; set; }

    /// <summary>
    /// Creates a project alias with a normalized value based on its type.
    /// </summary>
    public static ProjectAlias Create(
        Guid projectId,
        string alias,
        AliasType aliasType,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        Guard.AgainstEmpty(projectId);
        var value = Guard.AgainstNullOrWhiteSpace(alias).Trim();

        return new ProjectAlias(id ?? Guid.NewGuid(), createdAtUtc ?? DateTimeOffset.UtcNow)
        {
            ProjectId = projectId,
            Alias = value,
            AliasType = aliasType,
            NormalizedAlias = NormalizeAlias(value, aliasType)
        };
    }

    /// <summary>
    /// Normalizes an alias according to its type.
    /// </summary>
    public static string NormalizeAlias(string alias, AliasType aliasType)
    {
        var trimmed = Guard.AgainstNullOrWhiteSpace(alias).Trim();
        return aliasType switch
        {
            AliasType.RepositoryPath => NormalizedPath.Normalize(trimmed),
            AliasType.RemoteUrl => NormalizedRemoteUrl.Normalize(trimmed),
            _ => trimmed.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectAlias"/> class.
    /// </summary>
    public ProjectAlias()
    {
    }

    private ProjectAlias(Guid id, DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
    }
}
