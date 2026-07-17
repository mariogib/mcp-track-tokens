namespace McpTrackTokens.Domain.Enums;

/// <summary>
/// Type of alias used to map external identifiers to a project.
/// </summary>
public enum AliasType
{
    /// <summary>Local repository path.</summary>
    RepositoryPath = 0,

    /// <summary>Repository folder or display name.</summary>
    RepositoryName = 1,

    /// <summary>Git remote URL.</summary>
    RemoteUrl = 2,

    /// <summary>Editor workspace name.</summary>
    WorkspaceName = 3,

    /// <summary>Manually entered alias.</summary>
    Manual = 4,

    /// <summary>External project identifier from another system.</summary>
    ExternalProjectId = 5
}
