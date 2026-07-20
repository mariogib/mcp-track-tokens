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
    [McpServerPrompt(Name = "analyse_project_ai_cost"), Description("Analyse project AI cost including usage, subscription allocation, and calculated token cost.")]
    public static string AnalyseProjectAiCost(
        [Description("Project name or id")] string project,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Analyse AI cost for project '{project}'{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" over {dateRange}")}.
            Use get_project_cost and get_usage_summary (and get project token-cost fields on those responses).
            Separate usage-based Cursor cost, subscription allocation, other provider cost, unallocated amounts, and calculatedTokenCost (Settings rate card × attributed tokens).
            When reported usage cost is $0 (Included/Free), emphasise calculatedTokenCost as the better spend proxy.
            """;

    /// <summary>
    /// Create a client usage report.
    /// </summary>
    [McpServerPrompt(Name = "create_client_usage_report"), Description("Create a client-facing AI usage and billing summary including calculated token cost.")]
    public static string CreateClientUsageReport(
        [Description("Client name")] string clientName,
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Create a client usage/billing summary for '{clientName}'{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" covering {dateRange}")}.
            Use generate_client_billing_summary (includes calculatedTokenCost) and optionally generate_client_token_cost / export_project_report.
            Present project breakdowns suitable for invoicing: totalAiCost, subscriptionAllocation, usageBasedCost, and calculatedTokenCost.
            """;

    /// <summary>
    /// Compare project efficiency.
    /// </summary>
    [McpServerPrompt(Name = "compare_project_efficiency"), Description("Compare efficiency across editors/projects using activity, AI cost, and calculated token cost.")]
    public static string CompareProjectEfficiency(
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Compare project/editor efficiency{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" for {dateRange}")}.
            Use compare_projects, get_project_activity, and get_project_cost.
            Contrast prompt counts, active time, agent duration, totalAiCost, and calculatedTokenCost intensity.
            """;

    /// <summary>
    /// Identify unallocated usage.
    /// </summary>
    [McpServerPrompt(Name = "identify_unallocated_usage"), Description("Identify unallocated imported usage and suggest attribution.")]
    public static string IdentifyUnallocatedUsage(
        [Description("Optional date range description")] string? dateRange = null)
        => $"""
            Identify unallocated imported usage{(string.IsNullOrWhiteSpace(dateRange) ? string.Empty : $" for {dateRange}")}.
            Use get_unallocated_usage (includes totalCalculatedTokenCost) and run_usage_reconciliation with dryRun=true first.
            Suggest high-confidence allocations and call out records needing manual review; include rate-card calculated impact where helpful.
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
    [McpServerPrompt(Name = "prepare_monthly_ai_cost_report"), Description("Prepare a monthly AI cost report across projects including calculated token cost.")]
    public static string PrepareMonthlyAiCostReport(
        [Description("Year")] int year,
        [Description("Month number 1-12")] int month)
        => $"""
            Prepare the monthly AI cost report for {year}-{month:D2}.
            Use get_tracking_status, get_usage_summary, get_project_cost for major projects, and export_project_report.
            Separate usage-based cost, subscription allocation, calculatedTokenCost (rate card × attributed tokens), and remaining unallocated usage (including totalCalculatedTokenCost).
            """;
}
