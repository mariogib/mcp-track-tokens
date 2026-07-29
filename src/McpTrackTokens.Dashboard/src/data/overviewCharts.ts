import { SHARED_CHART_KEYS, SHARED_CHARTS, type ChartDef } from './chartDefs';

export const OVERVIEW_CHART_KEYS = [...SHARED_CHART_KEYS, 'activity-by-project'] as const;

export type OverviewChartKey = (typeof OVERVIEW_CHART_KEYS)[number];

export type OverviewChartKind = ChartDef['kind'];

export type OverviewChartDef = ChartDef<OverviewChartKey>;

export const OVERVIEW_CHARTS: Record<OverviewChartKey, OverviewChartDef> = {
  ...SHARED_CHARTS,
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
