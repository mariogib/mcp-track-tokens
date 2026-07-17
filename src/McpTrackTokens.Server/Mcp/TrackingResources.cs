using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Server.Hosting;
using McpTrackTokens.Server.Mapping;

namespace McpTrackTokens.Server.Mcp;

/// <summary>
/// MCP resources exposing tracking status and report snapshots.
/// </summary>
[McpServerResourceType]
public static class TrackingResources
{
    /// <summary>
    /// Current tracking status.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://status", Name = "Tracking Status", MimeType = "application/json")]
    [Description("Current tracking status snapshot.")]
    public static async Task<string> Status(IReportService reports, CancellationToken cancellationToken = default)
        => Serialize(await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Registered projects.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://projects", Name = "Projects", MimeType = "application/json")]
    [Description("List of registered projects.")]
    public static async Task<string> Projects(IProjectRepository projects, CancellationToken cancellationToken = default)
    {
        var list = await projects.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return Serialize(list.Select(ProjectMapper.ToDto).ToList());
    }

    /// <summary>
    /// A single project by id.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://projects/{id}", Name = "Project", MimeType = "application/json")]
    [Description("Project detail by id.")]
    public static async Task<string> Project(
        IProjectRepository projects,
        [Description("Project id")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var projectId))
        {
            return Serialize(new { error = "Invalid project id." });
        }

        var project = await projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        return Serialize(project is null ? null : ProjectMapper.ToDto(project));
    }

    /// <summary>
    /// Recent activity summary.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://activity", Name = "Activity", MimeType = "application/json")]
    [Description("Activity summary for the last 30 days.")]
    public static async Task<string> Activity(IReportService reports, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        return Serialize(await reports.GetActivitySummaryAsync(null, from, to, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Usage attribution summary.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://usage", Name = "Usage", MimeType = "application/json")]
    [Description("Usage attribution for the last 30 days.")]
    public static async Task<string> Usage(IReportService reports, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        return Serialize(await reports.GetUsageAttributionAsync(from, to, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Model cost summary.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://cost", Name = "Cost", MimeType = "application/json")]
    [Description("Model cost summary for the last 30 days.")]
    public static async Task<string> Cost(IReportService reports, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        return Serialize(await reports.GetModelCostAsync(from, to, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Unallocated activity items.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://unallocated/activity", Name = "Unallocated Activity", MimeType = "application/json")]
    [Description("Unallocated activity for the last 30 days.")]
    public static async Task<string> UnallocatedActivity(IReportService reports, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        return Serialize(await reports.GetUnallocatedActivityAsync(from, to, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Unallocated usage items.
    /// </summary>
    [McpServerResource(UriTemplate = "mcp-track-tokens://unallocated/usage", Name = "Unallocated Usage", MimeType = "application/json")]
    [Description("Unallocated usage for the last 30 days.")]
    public static async Task<string> UnallocatedUsage(IReportService reports, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-30);
        return Serialize(await reports.GetUnallocatedUsageAsync(from, to, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    private static string Serialize(object? value)
        => JsonSerializer.Serialize(value, TrackingHost.JsonOptions);
}
