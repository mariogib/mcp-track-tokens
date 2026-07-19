using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Server.Mapping;

/// <summary>
/// Maps domain projects to API DTOs.
/// </summary>
public static class ProjectMapper
{
    public static ProjectDto ToDto(Project project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Slug = project.Slug,
        ClientName = project.ClientName,
        BillingCode = project.BillingCode,
        Currency = project.Currency,
        PrimaryRepositoryPath = project.PrimaryRepositoryPath,
        PrimaryRemoteUrl = project.PrimaryRemoteUrl,
        IsActive = project.IsActive,
        CreatedAtUtc = project.CreatedAtUtc,
        UpdatedAtUtc = project.UpdatedAtUtc
    };

    public static ProjectDetailDto ToDetailDto(
        Project project,
        IReadOnlyList<ProjectRepository> repositories,
        IReadOnlyList<ProjectAlias> aliases,
        ActivitySummaryDto? activity = null,
        UsageSummaryDto? usage = null,
        CostSummaryDto? cost = null)
        => new()
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            ClientName = project.ClientName,
            BillingCode = project.BillingCode,
            Currency = project.Currency,
            PrimaryRepositoryPath = project.PrimaryRepositoryPath,
            PrimaryRemoteUrl = project.PrimaryRemoteUrl,
            IsActive = project.IsActive,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            Repositories = repositories.Select(r => new ProjectRepositoryDto
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                LocalPath = r.LocalPath,
                NormalizedPath = r.NormalizedPath,
                RemoteUrl = r.RemoteUrl,
                NormalizedRemoteUrl = r.NormalizedRemoteUrl,
                DefaultBranch = r.DefaultBranch,
                IsActive = r.IsActive
            }).ToList(),
            Aliases = aliases.Select(a => new ProjectAliasDto
            {
                Id = a.Id,
                ProjectId = a.ProjectId,
                Alias = a.Alias,
                NormalizedAlias = a.NormalizedAlias,
                AliasType = a.AliasType.ToString()
            }).ToList(),
            Activity = activity,
            Usage = usage,
            Cost = cost
        };
}

/// <summary>
/// Maps timesheet entries to API response shapes.
/// </summary>
public static class TimesheetMapper
{
    public static object ToDto(TimesheetEntry entry) => new
    {
        entry.Id,
        entry.ProjectId,
        entry.StartedAtUtc,
        entry.EndedAtUtc,
        entry.Notes,
        isOpen = entry.EndedAtUtc is null,
        entry.CreatedAtUtc,
        entry.UpdatedAtUtc
    };
}

/// <summary>
/// Maps editor sessions to anonymous-safe response shapes.
/// </summary>
public static class SessionMapper
{
    public static object ToDto(EditorSession session) => new
    {
        session.Id,
        session.ProjectId,
        editor = session.Editor.ToString(),
        session.EditorVersion,
        session.MachineName,
        session.UserName,
        session.WorkspacePath,
        session.RepositoryPath,
        session.RemoteUrl,
        session.Branch,
        session.ExternalSessionId,
        session.StartedAtUtc,
        session.EndedAtUtc,
        lastActivityAtUtc = session.LastActivityAtUtc,
        lastHeartbeatAtUtc = session.LastActivityAtUtc,
        status = session.Status.ToString(),
        isActive = session.Status == SessionStatus.Active && session.EndedAtUtc is null,
        session.UpdatedAtUtc
    };
}
