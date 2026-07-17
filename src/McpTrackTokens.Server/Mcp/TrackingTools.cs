using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Server.Hosting;
using McpTrackTokens.Server.Mapping;

namespace McpTrackTokens.Server.Mcp;

/// <summary>
/// MCP tools that map onto application tracking services.
/// </summary>
[McpServerToolType]
public static class TrackingTools
{
    /// <summary>
    /// Registers a new project for activity and cost tracking.
    /// </summary>
    [McpServerTool(Name = "register_project"), Description("Registers a new project for activity and cost tracking.")]
    public static async Task<string> RegisterProject(
        IProjectDetectionService projects,
        [Description("Project display name")] string name,
        [Description("Optional slug")] string? slug = null,
        [Description("Optional client name")] string? clientName = null,
        [Description("Optional billing code")] string? billingCode = null,
        [Description("Optional ISO currency")] string? currency = null,
        [Description("Optional repository path")] string? repositoryPath = null,
        [Description("Optional remote URL")] string? remoteUrl = null,
        CancellationToken cancellationToken = default)
    {
        var result = await projects.RegisterAsync(
            new CreateProjectRequest
            {
                Name = name,
                Slug = slug,
                ClientName = clientName,
                BillingCode = billingCode,
                Currency = currency,
                RepositoryPath = repositoryPath,
                RemoteUrl = remoteUrl
            },
            cancellationToken).ConfigureAwait(false);
        return Serialize(result);
    }

    /// <summary>
    /// Detects the current project from workspace or repository context.
    /// </summary>
    [McpServerTool(Name = "detect_current_project"), Description("Detects the current project from workspace or repository context.")]
    public static async Task<string> DetectCurrentProject(
        IProjectDetectionService projects,
        [Description("Workspace path")] string? workspacePath = null,
        [Description("Repository path")] string? repositoryPath = null,
        [Description("Remote URL")] string? remoteUrl = null,
        [Description("Active file path")] string? activeFilePath = null,
        [Description("Create project when missing")] bool? createIfMissing = null,
        CancellationToken cancellationToken = default)
    {
        var project = await projects
            .DetectAsync(workspacePath, repositoryPath, remoteUrl, activeFilePath, createIfMissing, cancellationToken)
            .ConfigureAwait(false);
        return Serialize(project is null ? null : ProjectMapper.ToDto(project));
    }

    /// <summary>
    /// Starts a tracked editor session for a project.
    /// </summary>
    [McpServerTool(Name = "start_project_session"), Description("Starts a tracked editor session for a project.")]
    public static async Task<string> StartProjectSession(
        IEventIngestionService ingestion,
        [Description("Editor name, e.g. Cursor or VsCode")] string editor,
        [Description("Optional project id")] Guid? projectId = null,
        [Description("Workspace path")] string? workspacePath = null,
        [Description("Repository path")] string? repositoryPath = null,
        [Description("Remote URL")] string? remoteUrl = null,
        [Description("External session id")] string? externalSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var session = await ingestion.StartSessionAsync(
            new SessionStartDto
            {
                ProjectId = projectId,
                Editor = editor,
                WorkspacePath = workspacePath,
                RepositoryPath = repositoryPath,
                RemoteUrl = remoteUrl,
                ExternalSessionId = externalSessionId
            },
            cancellationToken).ConfigureAwait(false);
        return Serialize(SessionMapper.ToDto(session));
    }

    /// <summary>
    /// Stops a tracked editor session.
    /// </summary>
    [McpServerTool(Name = "stop_project_session"), Description("Stops a tracked editor session.")]
    public static async Task<string> StopProjectSession(
        IEventIngestionService ingestion,
        [Description("Session id")] Guid? sessionId = null,
        [Description("External session id")] string? externalSessionId = null,
        [Description("Editor name")] string? editor = null,
        CancellationToken cancellationToken = default)
    {
        var session = await ingestion.EndSessionAsync(
            new SessionEndDto
            {
                SessionId = sessionId,
                ExternalSessionId = externalSessionId,
                Editor = editor
            },
            cancellationToken).ConfigureAwait(false);
        return Serialize(session is null ? null : SessionMapper.ToDto(session));
    }

    /// <summary>
    /// Returns the current tracking status snapshot.
    /// </summary>
    [McpServerTool(Name = "get_tracking_status"), Description("Returns the current tracking status snapshot.")]
    public static async Task<string> GetTrackingStatus(
        IReportService reports,
        CancellationToken cancellationToken = default)
        => Serialize(await reports.GetTrackingStatusAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Returns project activity for a date range.
    /// </summary>
    [McpServerTool(Name = "get_project_activity"), Description("Returns project activity for a date range.")]
    public static async Task<string> GetProjectActivity(
        IReportService reports,
        [Description("Project id")] Guid projectId,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports.GetProjectActivityAsync(projectId, from, to, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Returns the prompt count for a project or overall in a date range.
    /// </summary>
    [McpServerTool(Name = "get_prompt_count"), Description("Returns the prompt count for a project or overall in a date range.")]
    public static async Task<string> GetPromptCount(
        IReportService reports,
        [Description("Optional project id")] Guid? projectId = null,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        var summary = await reports.GetActivitySummaryAsync(projectId, from, to, cancellationToken).ConfigureAwait(false);
        return Serialize(new { summary.PromptCount, fromUtc = from, toUtc = to, projectId });
    }

    /// <summary>
    /// Returns active project time for a project.
    /// </summary>
    [McpServerTool(Name = "get_project_time"), Description("Returns active project time for a project.")]
    public static async Task<string> GetProjectTime(
        IReportService reports,
        [Description("Project id")] Guid projectId,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        var summary = await reports.GetActivitySummaryAsync(projectId, from, to, cancellationToken).ConfigureAwait(false);
        return Serialize(new
        {
            projectId,
            summary.ActiveProjectTimeSeconds,
            summary.AgentDurationMilliseconds,
            fromUtc = from,
            toUtc = to
        });
    }

    /// <summary>
    /// Returns project AI cost separating usage and subscription allocation.
    /// </summary>
    [McpServerTool(Name = "get_project_cost"), Description("Returns project AI cost separating usage and subscription allocation.")]
    public static async Task<string> GetProjectCost(
        IReportService reports,
        [Description("Project id")] Guid projectId,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Include subscription allocation")] bool includeSubscriptionAllocation = true,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports
            .GetProjectCostAsync(projectId, from, to, includeSubscriptionAllocation, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Returns imported usage attribution for a project or overall.
    /// </summary>
    [McpServerTool(Name = "get_usage_summary"), Description("Returns imported usage attribution for a project or overall.")]
    public static async Task<string> GetUsageSummary(
        IReportService reports,
        [Description("Optional project id")] Guid? projectId = null,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports.GetUsageAttributionAsync(from, to, projectId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Lists activity events that are not attributed to a project.
    /// </summary>
    [McpServerTool(Name = "get_unallocated_activity"), Description("Lists activity events that are not attributed to a project.")]
    public static async Task<string> GetUnallocatedActivity(
        IReportService reports,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Optional limit")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports.GetUnallocatedActivityAsync(from, to, limit, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Assigns unallocated activity events to a project.
    /// </summary>
    [McpServerTool(Name = "assign_activity_to_project"), Description("Assigns unallocated activity events to a project.")]
    public static async Task<string> AssignActivityToProject(
        IActivityEventRepository events,
        IUnitOfWork unitOfWork,
        [Description("Project id")] Guid projectId,
        [Description("Activity event ids")] Guid[] eventIds,
        CancellationToken cancellationToken = default)
    {
        await events.AssignProjectAsync(
            eventIds,
            projectId,
            AttributionMethod.Manual,
            AttributionConfidence.High,
            cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Serialize(new { assigned = eventIds.Length, projectId });
    }

    /// <summary>
    /// Lists imported usage that is not allocated to a project.
    /// </summary>
    [McpServerTool(Name = "get_unallocated_usage"), Description("Lists imported usage that is not allocated to a project.")]
    public static async Task<string> GetUnallocatedUsage(
        IReportService reports,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Optional limit")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports.GetUnallocatedUsageAsync(from, to, limit, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Manually allocates a usage record across projects.
    /// </summary>
    [McpServerTool(Name = "allocate_usage"), Description("Manually allocates a usage record across projects.")]
    public static async Task<string> AllocateUsage(
        IAttributionEngine attribution,
        [Description("External usage record id")] Guid usageRecordId,
        [Description("Project id receiving the allocation")] Guid projectId,
        [Description("Allocation percentage 0-100")] decimal percentage = 100m,
        [Description("Optional reason")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await attribution.AttributeManualAsync(
            new AllocationRequestDto
            {
                UsageRecordId = usageRecordId,
                Reason = reason,
                ProjectAllocations =
                [
                    new ProjectAllocationShareDto
                    {
                        ProjectId = projectId,
                        Percentage = percentage
                    }
                ]
            },
            cancellationToken).ConfigureAwait(false);
        return Serialize(result);
    }

    /// <summary>
    /// Runs usage reconciliation over a date range.
    /// </summary>
    [McpServerTool(Name = "run_usage_reconciliation"), Description("Runs usage reconciliation over a date range.")]
    public static async Task<string> RunUsageReconciliation(
        IReconciliationService reconciliation,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Dry run without persisting")] bool dryRun = false,
        [Description("Include low-confidence attributions")] bool includeLowConfidence = false,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reconciliation.RunAsync(
            new ReconciliationRequestDto
            {
                FromUtc = from,
                ToUtc = to,
                DryRun = dryRun,
                IncludeLowConfidence = includeLowConfidence
            },
            cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Imports a Cursor usage export file.
    /// </summary>
    [McpServerTool(Name = "import_cursor_usage"), Description("Imports a Cursor usage export file.")]
    public static async Task<string> ImportCursorUsage(
        ICursorUsageImporter importer,
        [Description("Absolute path to the Cursor usage file")] string filePath,
        [Description("Optional format override")] string? format = null,
        [Description("Dry run without persisting")] bool dryRun = false,
        [Description("Force re-import of a previously hashed file")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        return Serialize(await importer.ImportAsync(
            new ImportCursorUsageRequestDto
            {
                FilePath = filePath,
                Format = format,
                DryRun = dryRun,
                Force = force
            },
            cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Exports a project report to an approved export directory.
    /// </summary>
    [McpServerTool(Name = "export_project_report"), Description("Exports a project report to an approved export directory.")]
    public static async Task<string> ExportProjectReport(
        IExportService export,
        [Description("Project id")] Guid projectId,
        [Description("Report type, e.g. project-activity or project-cost")] string reportType = "project-cost",
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Optional output directory")] string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await export.ExportAsync(
            new ExportRequestDto
            {
                ProjectId = projectId,
                ReportType = reportType,
                FromUtc = from,
                ToUtc = to,
                OutputDirectory = outputDirectory,
                Format = ExportFormat.Json
            },
            cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Generates a client billing summary across projects.
    /// </summary>
    [McpServerTool(Name = "generate_client_billing_summary"), Description("Generates a client billing summary across projects.")]
    public static async Task<string> GenerateClientBillingSummary(
        IReportService reports,
        [Description("Client name")] string clientName,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await reports.GetClientCostAsync(clientName, from, to, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Compares editor activity metrics across the date range.
    /// </summary>
    [McpServerTool(Name = "compare_projects"), Description("Compares editor activity metrics across the date range.")]
    public static async Task<string> CompareProjects(
        IReportService reports,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        var editors = await reports.GetEditorComparisonAsync(from, to, cancellationToken).ConfigureAwait(false);
        var models = await reports.GetModelCostAsync(from, to, cancellationToken).ConfigureAwait(false);
        return Serialize(new { editors, models });
    }

    /// <summary>
    /// Recalculates activity windows for a project or overall.
    /// </summary>
    [McpServerTool(Name = "recalculate_activity_windows"), Description("Recalculates activity windows for a project or overall.")]
    public static async Task<string> RecalculateActivityWindows(
        IActivityWindowService windows,
        [Description("Optional project id")] Guid? projectId = null,
        [Description("Range start UTC")] DateTimeOffset? fromUtc = null,
        [Description("Range end UTC")] DateTimeOffset? toUtc = null,
        [Description("Optional inactivity threshold minutes")] int? inactivityThresholdMinutes = null,
        [Description("Dry run without persisting")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc);
        return Serialize(await windows.RecalculateAsync(
            projectId,
            from,
            to,
            inactivityThresholdMinutes,
            dryRun,
            cancellationToken).ConfigureAwait(false));
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var to = toUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var from = fromUtc?.ToUniversalTime() ?? to.AddDays(-30);
        return from <= to ? (from, to) : (to, from);
    }

    private static string Serialize(object? value)
        => JsonSerializer.Serialize(value, TrackingHost.JsonOptions);
}
