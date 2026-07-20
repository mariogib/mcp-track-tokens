export const OVERVIEW_CHART_KEYS = [
  'prompts-day',
  'active-time-day',
  'agent-duration-day',
  'cost-day',
  'tokens-day',
  'cost-by-model',
  'calculated-cost-by-model',
  'activity-by-project',
] as const;

export type OverviewChartKey = (typeof OVERVIEW_CHART_KEYS)[number];

export type OverviewChartKind = 'line' | 'pie' | 'bar';

export type OverviewChartDef = {
  key: OverviewChartKey;
  title: string;
  kind: OverviewChartKind;
  filter?: 'model' | 'project' | 'day';
  yLabel?: string;
  valueLabel?: string;
};

export const OVERVIEW_CHARTS: Record<OverviewChartKey, OverviewChartDef> = {
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
  'activity-by-project': {
    key: 'activity-by-project',
    title: 'Activity by project',
    kind: 'bar',
    filter: 'project',
    valueLabel: 'Prompts',
  },
};

export function isOverviewChartKey(value: string | undefined): value is OverviewChartKey {
  return !!value && (OVERVIEW_CHART_KEYS as readonly string[]).includes(value);
}

export function overviewChartPath(
  chartKey: OverviewChartKey,
  options?: { range?: string; from?: string; to?: string },
): string {
  const params = new URLSearchParams();
  const range = options?.range ?? '30d';
  params.set('range', range);
  if (range === 'custom') {
    if (options?.from) params.set('from', options.from);
    if (options?.to) params.set('to', options.to);
  }
  return `/charts/${chartKey}?${params.toString()}`;
}
