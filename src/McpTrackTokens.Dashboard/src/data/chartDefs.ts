export type ChartKind = 'line' | 'pie' | 'bar';

export type ChartFilter = 'model' | 'project' | 'branch' | 'day';

export type ChartDef<TKey extends string = string> = {
  key: TKey;
  title: string;
  kind: ChartKind;
  filter?: ChartFilter;
  yLabel?: string;
  valueLabel?: string;
};

/** Chart keys shared by overview and project detail pages. */
export const SHARED_CHART_KEYS = [
  'prompts-day',
  'active-time-day',
  'agent-duration-day',
  'cost-day',
  'tokens-day',
  'cost-by-model',
  'calculated-cost-by-model',
] as const;

export type SharedChartKey = (typeof SHARED_CHART_KEYS)[number];

function sharedChart<TKey extends SharedChartKey>(
  key: TKey,
  def: Omit<ChartDef<TKey>, 'key'>,
): ChartDef<TKey> {
  return { key, ...def };
}

export const SHARED_CHARTS: Record<SharedChartKey, ChartDef<SharedChartKey>> = {
  'prompts-day': sharedChart('prompts-day', {
    title: 'Prompts / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Prompts',
  }),
  'active-time-day': sharedChart('active-time-day', {
    title: 'Active time / day (minutes)',
    kind: 'line',
    filter: 'day',
    yLabel: 'Minutes',
  }),
  'agent-duration-day': sharedChart('agent-duration-day', {
    title: 'Agent duration / day (minutes)',
    kind: 'line',
    filter: 'day',
    yLabel: 'Minutes',
  }),
  'cost-day': sharedChart('cost-day', {
    title: 'Cost / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Cost',
  }),
  'tokens-day': sharedChart('tokens-day', {
    title: 'Tokens / day',
    kind: 'line',
    filter: 'day',
    yLabel: 'Tokens',
  }),
  'cost-by-model': sharedChart('cost-by-model', {
    title: 'Cost by model',
    kind: 'pie',
    filter: 'model',
    valueLabel: 'Cost',
  }),
  'calculated-cost-by-model': sharedChart('calculated-cost-by-model', {
    title: 'Calculated cost by model',
    kind: 'pie',
    filter: 'model',
    valueLabel: 'Token cost',
  }),
};

export function isSharedChartKey(value: string | undefined): value is SharedChartKey {
  return !!value && (SHARED_CHART_KEYS as readonly string[]).includes(value);
}

export function lineValueKey(chartKey: string): string {
  switch (chartKey) {
    case 'active-time-day':
      return 'activeMinutes';
    case 'agent-duration-day':
      return 'agentMinutes';
    case 'cost-day':
      return 'cost';
    case 'tokens-day':
      return 'tokens';
    case 'prompts-day':
    default:
      return 'prompts';
  }
}
