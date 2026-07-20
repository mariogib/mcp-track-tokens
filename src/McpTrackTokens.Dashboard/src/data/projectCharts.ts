export const PROJECT_CHART_KEYS = [
  'prompts-day',
  'active-time-day',
  'agent-duration-day',
  'cost-day',
  'tokens-day',
  'cost-by-model',
  'calculated-cost-by-model',
  'activity-by-branch',
] as const;

export type ProjectChartKey = (typeof PROJECT_CHART_KEYS)[number];

export type ProjectChartKind = 'line' | 'pie' | 'bar';

export type ProjectChartDef = {
  key: ProjectChartKey;
  title: string;
  kind: ProjectChartKind;
  /** Extra filter control on the detail page. */
  filter?: 'model' | 'branch' | 'day';
  yLabel?: string;
  valueLabel?: string;
};

export const PROJECT_CHARTS: Record<ProjectChartKey, ProjectChartDef> = {
  'prompts-day': {
    key: 'prompts-day',
    title: 'Prompts / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Prompts',
  },
  'active-time-day': {
    key: 'active-time-day',
    title: 'Active time / day (minutes)',
    kind: 'line',
    filter: 'day',
    yLabel: 'Minutes',
  },
  'agent-duration-day': {
    key: 'agent-duration-day',
    title: 'Agent duration / day (minutes)',
    kind: 'line',
    filter: 'day',
    yLabel: 'Minutes',
  },
  'cost-day': {
    key: 'cost-day',
    title: 'Cost / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Cost',
  },
  'tokens-day': {
    key: 'tokens-day',
    title: 'Tokens / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Tokens',
  },
  'cost-by-model': {
    key: 'cost-by-model',
    title: 'Cost by model',
    kind: 'pie',
    filter: 'model',
    valueLabel: 'Cost',
  },
  'calculated-cost-by-model': {
    key: 'calculated-cost-by-model',
    title: 'Calculated cost by model',
    kind: 'pie',
    filter: 'model',
    valueLabel: 'Token cost',
  },
  'activity-by-branch': {
    key: 'activity-by-branch',
    title: 'Activity by branch',
    kind: 'bar',
    filter: 'branch',
    valueLabel: 'Prompts',
  },
};

export function isProjectChartKey(value: string | undefined): value is ProjectChartKey {
  return !!value && (PROJECT_CHART_KEYS as readonly string[]).includes(value);
}

export function projectChartPath(
  projectId: string,
  chartKey: ProjectChartKey,
  range = '30d',
): string {
  return `/projects/${projectId}/charts/${chartKey}?range=${encodeURIComponent(range)}`;
}
