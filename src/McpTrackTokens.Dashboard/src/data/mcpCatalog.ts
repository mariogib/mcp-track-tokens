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
    description: 'Compares editor activity metrics across the date range.',
    group: 'Status & activity',
  },
  {
    name: 'get_project_cost',
    description: 'Returns project AI cost separating usage and subscription allocation.',
    group: 'Cost & usage',
  },
  {
    name: 'get_usage_summary',
    description: 'Returns imported usage attribution for a project or overall.',
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
    description: 'Lists imported usage that is not allocated to a project.',
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
    description: 'Generates a client billing summary across projects.',
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
    description: 'Usage attribution for the last 30 days.',
  },
  {
    name: 'Cost',
    uri: 'mcp-track-tokens://cost',
    description: 'Model cost summary for the last 30 days.',
  },
  {
    name: 'Unallocated Activity',
    uri: 'mcp-track-tokens://unallocated/activity',
    description: 'Unallocated activity for the last 30 days.',
  },
  {
    name: 'Unallocated Usage',
    uri: 'mcp-track-tokens://unallocated/usage',
    description: 'Unallocated usage for the last 30 days.',
  },
];

export const MCP_PROMPTS: McpPromptHelp[] = [
  {
    name: 'analyse_project_activity',
    description: 'Analyse project activity patterns and highlight anomalies.',
    args: 'project, dateRange?',
  },
  {
    name: 'analyse_project_ai_cost',
    description: 'Analyse project AI cost including usage and subscription allocation.',
    args: 'project, dateRange?',
  },
  {
    name: 'create_client_usage_report',
    description: 'Create a client-facing AI usage and billing summary.',
    args: 'clientName, dateRange?',
  },
  {
    name: 'compare_project_efficiency',
    description: 'Compare efficiency across editors/projects using activity and cost metrics.',
    args: 'dateRange?',
  },
  {
    name: 'identify_unallocated_usage',
    description: 'Identify unallocated imported usage and suggest attribution.',
    args: 'dateRange?',
  },
  {
    name: 'identify_activity_anomalies',
    description: 'Identify unusual activity patterns or unallocated events.',
    args: 'project?, dateRange?',
  },
  {
    name: 'prepare_monthly_ai_cost_report',
    description: 'Prepare a monthly AI cost report across projects.',
    args: 'year, month',
  },
];
