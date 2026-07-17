using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpTrackTokens.Server.Mcp;

/// <summary>
/// MCP prompt templates for common tracking analyses.
/// </summary>
[McpServerPromptType]
public static class TrackingPrompts
{
    /// <summary>
    /// Analyse project activity patterns.
    /// </summary>
    [McpServerPrompt(Name = "analyse_project_activity"), Description("Analyse project activity patterns and highlight anomalies.")]
    public static string AnalyseProjectActivity(
        [Description("Project name or id")] string project,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Analyse MCP Track Tokens activity for project '{project}'{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" over {dateRange}")}.
            Use get_project_activity, get_prompt_count, and get_project_time.
            Summarise prompt volume, agent runs, active project time, failures, and notable day-to-day changes.
            """;

    /// <summary>
    /// Analyse project AI cost.
    /// </summary>
    [McpServerPrompt(Name = "analyse_project_ai_cost"), Description("Analyse project AI cost including usage and subscription allocation.")]
    public static string AnalyseProjectAiCost(
        [Description("Project name or id")] string project,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Analyse AI cost for project '{project}'{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" over {dateRange}")}.
            Use get_project_cost and get_usage_summary.
            Separate usage-based Cursor cost, subscription allocation, other provider cost, and unallocated amounts.
            """;

    /// <summary>
    /// Create a client usage report.
    /// </summary>
    [McpServerPrompt(Name = "create_client_usage_report"), Description("Create a client-facing AI usage and billing summary.")]
    public static string CreateClientUsageReport(
        [Description("Client name")] string clientName,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Create a client usage/billing summary for '{clientName}'{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" covering {dateRange}")}.
            Use generate_client_billing_summary and export_project_report where helpful.
            Present project breakdowns suitable for invoicing discussion.
            """;

    /// <summary>
    /// Compare project efficiency.
    /// </summary>
    [McpServerPrompt(Name = "compare_project_efficiency"), Description("Compare efficiency across editors/projects using activity and cost metrics.")]
    public static string CompareProjectEfficiency(
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Compare project/editor efficiency{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" for {dateRange}")}.
            Use compare_projects, get_project_activity, and get_project_cost.
            Contrast prompt counts, active time, agent duration, and AI cost intensity.
            """;

    /// <summary>
    /// Identify unallocated usage.
    /// </summary>
    [McpServerPrompt(Name = "identify_unallocated_usage"), Description("Identify unallocated imported usage and suggest attribution.")]
    public static string IdentifyUnallocatedUsage(
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Identify unallocated imported usage{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" for {dateRange}")}.
            Use get_unallocated_usage and run_usage_reconciliation with dryRun=true first.
            Suggest high-confidence allocations and call out records needing manual review.
            """;

    /// <summary>
    /// Identify activity anomalies.
    /// </summary>
    [McpServerPrompt(Name = "identify_activity_anomalies"), Description("Identify unusual activity patterns or unallocated events.")]
    public static string IdentifyActivityAnomalies(
        [Description("Optional project name or id")] string? project = null,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Identify activity anomalies{(project is null ? string.Empty : $" for project '{project}'")}{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" over {dateRange}")}.
            Use get_unallocated_activity, get_tracking_status, and get_project_activity.
            Highlight spikes, gaps, failures, and unallocated sessions.
            """;

    /// <summary>
    /// Prepare a monthly AI cost report.
    /// </summary>
    [McpServerPrompt(Name = "prepare_monthly_ai_cost_report"), Description("Prepare a monthly AI cost report across projects.")]
    public static string PrepareMonthlyAiCostReport(
        [Description("Year")] int year,
        [Description("Month number 1-12")] int month)
        => $"""
            Prepare the monthly AI cost report for {year}-{month:D2}.
            Use get_tracking_status, get_usage_summary, get_project_cost for major projects, and export_project_report.
            Separate usage-based cost from subscription allocation and list remaining unallocated usage.
            """;
}
