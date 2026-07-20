import { useQueries } from '@tanstack/react-query';
import { api } from './client';
import { queryKeys, useProjectsQuery } from './hooks';
import {
  aggregateActivityReports,
  aggregateCostReports,
} from '../utils/aggregateProjectReports';

export function useAggregatedOverviewCharts(fromUtc: string, toUtc: string) {
  const projects = useProjectsQuery();
  const projectIds = projects.data?.map((p) => p.id) ?? [];

  const activityQueries = useQueries({
    queries: projectIds.map((id) => ({
      queryKey: queryKeys.projectActivity(id, fromUtc, toUtc),
      queryFn: ({ signal }: { signal?: AbortSignal }) =>
        api.getProjectActivity(id, fromUtc, toUtc, signal),
      enabled: projectIds.length > 0,
    })),
  });

  const costQueries = useQueries({
    queries: projectIds.map((id) => ({
      queryKey: queryKeys.projectCost(id, fromUtc, toUtc),
      queryFn: ({ signal }: { signal?: AbortSignal }) =>
        api.getProjectCost(id, fromUtc, toUtc, signal),
      enabled: projectIds.length > 0,
    })),
  });

  const activityReports = activityQueries.flatMap((q) => (q.data ? [q.data] : []));
  const costReports = costQueries.flatMap((q) => (q.data ? [q.data] : []));
  const aggregatedActivity = aggregateActivityReports(activityReports);
  const aggregatedCost = aggregateCostReports(costReports);

  const projectSeries = activityReports
    .map((report) => ({
      projectId: report.projectId,
      name: report.projectName || report.projectSlug || 'Unknown',
      prompts: report.promptCount,
    }))
    .sort((a, b) => b.prompts - a.prompts);

  const isLoading =
    projects.isLoading ||
    (projectIds.length > 0 &&
      (activityQueries.some((q) => q.isLoading) || costQueries.some((q) => q.isLoading)));

  const error =
    projects.error ||
    activityQueries.find((q) => q.error)?.error ||
    costQueries.find((q) => q.error)?.error ||
    null;

  return {
    projects,
    projectIds,
    activityReports,
    costReports,
    aggregatedActivity,
    aggregatedCost,
    projectSeries,
    isLoading,
    error,
  };
}
