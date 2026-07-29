import { SHARED_CHART_KEYS, SHARED_CHARTS, type ChartDef } from './chartDefs';

export const PROJECT_CHART_KEYS = [...SHARED_CHART_KEYS, 'activity-by-branch'] as const;

export type ProjectChartKey = (typeof PROJECT_CHART_KEYS)[number];

export type ProjectChartKind = ChartDef['kind'];

export type ProjectChartDef = ChartDef<ProjectChartKey>;

export const PROJECT_CHARTS: Record<ProjectChartKey, ProjectChartDef> = {
  ...SHARED_CHARTS,
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
