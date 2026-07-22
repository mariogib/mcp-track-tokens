export type McpToolHelp = {
  name: string;
  description: string;
  group: string;
};

export type McpResourceHelp = {
  name: string;
  uri: string;
  description: string;
};

export type McpPromptHelp = {
  name: string;
  description: string;
  args: string;
  /** Sample user message you can paste into the agent chat. */
  example: string;
};

export const MCP_TOOLS: McpToolHelp[] = [
  {
    name: 'register_project',
    description: 'Registers a new project for activity and cost tracking.',
    group: 'Projects / sessions',
  },
  {
    name: 'detect_current_project',
    description: 'Detects the current project from workspace or repository context.',
    group: 'Projects / sessions',
  },
  {
    name: 'list_projects',
    description: 'Lists all registered projects with the path to each project root.',
    group: 'Projects / sessions',
  },
  {
    name: 'start_project_session',
    description: 'Starts a tracked editor session for a project.',
    group: 'Projects / sessions',
  },
  {
    name: 'stop_project_session',
    description: 'Stops a tracked editor session.',
    group: 'Projects / sessions',
  },
  {
    name: 'start_timesheet',
    description:
      'Starts a timesheet entry for the current Cursor project. Defaults start time to now and closes any open entry first. Category defaults to Work.',
    group: 'Timesheet',
  },
  {
    name: 'end_timesheet',
    description:
      'Ends the open timesheet entry for the current Cursor project. Defaults end time to now and can append a note.',
    group: 'Timesheet',
  },
  {
    name: 'get_tracking_status',
    description: 'Returns the current tracking status snapshot.',
    group: 'Status & activity',
  },
  {
    name: 'check_cursor_hooks',
    description:
      'Checks whether Cursor hooks are installed and use event names compatible with the installed Cursor version. Inspects ~/.cursor/hooks.json, installed mcp-track-tokens-hooks scripts, Cursor app version, and recent Cursor ingest activity. Completes the check by ingesting a Heartbeat probe event stamped with the detected Cursor version.',
    group: 'Status & activity',
  },
  {
    name: 'get_project_activity',
    description: 'Returns project activity for a date range.',
    group: 'Status & activity',
  },
  {
    name: 'get_prompt_count',
    description: 'Returns the prompt count for a project or overall in a date range.',
    group: 'Status & activity',
  },
  {
    name: 'get_project_time',
    description: 'Returns active project time for a project.',
    group: 'Status & activity',
  },
  {
    name: 'recalculate_activity_windows',
    description: 'Recalculates activity windows for a project or overall.',
    group: 'Status & activity',
  },
  {
    name: 'compare_projects',
    description:
      'Compares editor activity and model cost metrics (including calculatedTokenCost) across the date range.',
    group: 'Status & activity',
  },
  {
    name: 'get_project_cost',
    description:
      'Returns project AI cost separating usage, subscription allocation, and calculatedTokenCost (rate card × attributed tokens).',
    group: 'Cost & usage',
  },
  {
    name: 'get_usage_summary',
    description:
      'Returns imported usage attribution for a project or overall, including totalCalculatedTokenCost.',
    group: 'Cost & usage',
  },
  {
    name: 'get_unallocated_activity',
    description: 'Lists activity events that are not attributed to a project.',
    group: 'Cost & usage',
  },
  {
    name: 'assign_activity_to_project',
    description: 'Assigns unallocated activity events to a project.',
    group: 'Cost & usage',
  },
  {
    name: 'get_unallocated_usage',
    description:
      'Lists imported usage that is not allocated to a project, including totalCalculatedTokenCost.',
    group: 'Cost & usage',
  },
  {
    name: 'allocate_usage',
    description: 'Manually allocates a usage record across projects.',
    group: 'Cost & usage',
  },
  {
    name: 'run_usage_reconciliation',
    description: 'Runs usage reconciliation over a date range.',
    group: 'Cost & usage',
  },
  {
    name: 'import_cursor_usage',
    description: 'Imports a Cursor usage export file.',
    group: 'Cost & usage',
  },
  {
    name: 'export_project_report',
    description: 'Exports a project report to an approved export directory.',
    group: 'Cost & usage',
  },
  {
    name: 'generate_client_billing_summary',
    description:
      'Generates a client billing summary across projects, including calculatedTokenCost (rate card × attributed tokens).',
    group: 'Cost & usage',
  },
  {
    name: 'generate_client_token_cost',
    description:
      'Estimates client token cost from Settings rate-card prices and attributed token usage.',
    group: 'Cost & usage',
  },
];

export const MCP_RESOURCES: McpResourceHelp[] = [
  {
    name: 'Tracking Status',
    uri: 'mcp-track-tokens://status',
    description: 'Current tracking status snapshot.',
  },
  {
    name: 'Projects',
    uri: 'mcp-track-tokens://projects',
    description: 'List of registered projects.',
  },
  {
    name: 'Project',
    uri: 'mcp-track-tokens://projects/{id}',
    description: 'Project detail by id.',
  },
  {
    name: 'Activity',
    uri: 'mcp-track-tokens://activity',
    description: 'Activity summary for the last 30 days.',
  },
  {
    name: 'Usage',
    uri: 'mcp-track-tokens://usage',
    description:
      'Usage attribution for the last 30 days, including totalCalculatedTokenCost (rate card × attributed tokens).',
  },
  {
    name: 'Cost',
    uri: 'mcp-track-tokens://cost',
    description:
      'Model cost summary for the last 30 days, including calculatedTokenCost from the Settings rate card.',
  },
  {
    name: 'Unallocated Activity',
    uri: 'mcp-track-tokens://unallocated/activity',
    description: 'Unallocated activity for the last 30 days.',
  },
  {
    name: 'Unallocated Usage',
    uri: 'mcp-track-tokens://unallocated/usage',
    description:
      'Unallocated usage for the last 30 days, including totalCalculatedTokenCost from the Settings rate card.',
  },
];

export const MCP_PROMPTS: McpPromptHelp[] = [
  {
    name: 'analyse_project_activity',
    description: 'Analyse project activity patterns and highlight anomalies.',
    args: 'project, dateRange?',
    example:
      "Analyse MCP Track Tokens activity for project 'MCP Track Tokens' over the last 30 days. Use get_project_activity, get_prompt_count, and get_project_time. Summarise prompt volume, agent runs, active project time, failures, and notable day-to-day changes.",
  },
  {
    name: 'analyse_project_ai_cost',
    description:
      'Analyse project AI cost including usage, subscription allocation, and calculated token cost.',
    args: 'project, dateRange?',
    example:
      "Analyse AI cost for project 'MCP Track Tokens' over the last 30 days. Use get_project_cost and get_usage_summary. Separate usage-based Cursor cost, subscription allocation, other provider cost, unallocated amounts, and calculatedTokenCost. When reported usage cost is $0, emphasise calculatedTokenCost.",
  },
  {
    name: 'create_client_usage_report',
    description:
      'Create a client-facing AI usage and billing summary including calculated token cost.',
    args: 'clientName, dateRange?',
    example:
      "Create a client usage/billing summary for 'LunarQ' covering July 2026. Use generate_client_billing_summary and present project breakdowns with totalAiCost, subscriptionAllocation, usageBasedCost, and calculatedTokenCost.",
  },
  {
    name: 'compare_project_efficiency',
    description:
      'Compare efficiency across editors/projects using activity, AI cost, and calculated token cost.',
    args: 'dateRange?',
    example:
      'Compare project/editor efficiency for the last 30 days. Use compare_projects, get_project_activity, and get_project_cost. Contrast prompt counts, active time, agent duration, totalAiCost, and calculatedTokenCost intensity.',
  },
  {
    name: 'identify_unallocated_usage',
    description: 'Identify unallocated imported usage and suggest attribution.',
    args: 'dateRange?',
    example:
      'Identify unallocated imported usage for the last 30 days. Use get_unallocated_usage and run_usage_reconciliation with dryRun=true first. Suggest high-confidence allocations and include rate-card calculated impact.',
  },
  {
    name: 'identify_activity_anomalies',
    description: 'Identify unusual activity patterns or unallocated events.',
    args: 'project?, dateRange?',
    example:
      "Identify activity anomalies for project 'MCP Track Tokens' over the last 30 days. Use get_unallocated_activity, get_tracking_status, and get_project_activity. Highlight spikes, gaps, failures, and unallocated sessions.",
  },
  {
    name: 'prepare_monthly_ai_cost_report',
    description:
      'Prepare a monthly AI cost report across projects including calculated token cost.',
    args: 'year, month',
    example:
      'Prepare the monthly AI cost report for 2026-07. Use get_tracking_status, get_usage_summary, get_project_cost for major projects, and export_project_report. Separate usage-based cost, subscription allocation, calculatedTokenCost, and remaining unallocated usage.',
  },
];
