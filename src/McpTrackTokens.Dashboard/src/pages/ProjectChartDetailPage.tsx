import { useMemo } from 'react';
import { Navigate, useParams } from 'react-router-dom';
import {
  useProjectActivityQuery,
  useProjectCostQuery,
  useProjectQuery,
} from '../api/hooks';
import { ChartDetailAnalysis } from '../components/ChartDetailAnalysis';
import { ErrorState, LoadingState } from '../components/States';
import {
  isProjectChartKey,
  PROJECT_CHARTS,
} from '../data/projectCharts';
import { useChartDetailSearchParams } from '../hooks/useChartDetailSearchParams';
import { Page } from '../layout/AppLayout';
import {
  buildDaySeries,
  buildModelCalculatedSeries,
  buildModelCostSeries,
  resolveDisplayCost,
} from '../utils/chartDetail';

export function ProjectChartDetailPage() {
  const { projectId, chartKey } = useParams();
  const search = useChartDetailSearchParams();
  const validKey = isProjectChartKey(chartKey) ? chartKey : null;

  const project = useProjectQuery(projectId);
  const activity = useProjectActivityQuery(projectId, search.range.fromUtc, search.range.toUtc);
  const cost = useProjectCostQuery(projectId, search.range.fromUtc, search.range.toUtc);

  const reportedTotalCost = cost.data?.totalAiCost ?? 0;
  const calculatedTotalCost = cost.data?.calculatedTokenCost ?? 0;
  const { displayTotalCost, usingCalculatedCost } = resolveDisplayCost(
    reportedTotalCost,
    calculatedTotalCost,
  );

  const daySeries = useMemo(
    () => buildDaySeries(activity.data?.byDay ?? [], displayTotalCost),
    [activity.data?.byDay, displayTotalCost],
  );

  const filteredDaySeries = useMemo(() => {
    if (!search.dayFilter) return daySeries;
    return daySeries.filter((row) => row.dayKey === search.dayFilter);
  }, [daySeries, search.dayFilter]);

  const modelCostSeries = useMemo(
    () => buildModelCostSeries(cost.data?.byModel ?? [], search.modelFilter),
    [cost.data?.byModel, search.modelFilter],
  );

  const modelCalculatedSeries = useMemo(
    () => buildModelCalculatedSeries(cost.data?.byModel ?? [], search.modelFilter),
    [cost.data?.byModel, search.modelFilter],
  );

  const branchSeries = useMemo(() => {
    const rows = (activity.data?.byBranch ?? []).map((b) => ({
      name: b.name || '(none)',
      prompts: b.promptCount,
    }));
    if (!search.branchFilter) return rows;
    return rows.filter((b) => b.name === search.branchFilter);
  }, [activity.data?.byBranch, search.branchFilter]);

  const modelOptions = useMemo(
    () =>
      [...new Set((cost.data?.byModel ?? []).map((m) => m.name || 'Unknown'))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [cost.data?.byModel],
  );

  const branchOptions = useMemo(
    () =>
      [...new Set((activity.data?.byBranch ?? []).map((b) => b.name || '(none)'))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [activity.data?.byBranch],
  );

  const dayOptions = useMemo(
    () =>
      [...new Set((activity.data?.byDay ?? []).map((row) => row.day))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [activity.data?.byDay],
  );

  if (!projectId || !validKey) {
    return <Navigate to={projectId ? `/projects/${projectId}` : '/projects'} replace />;
  }

  const def = PROJECT_CHARTS[validKey];
  const loading = project.isLoading || activity.isLoading || cost.isLoading;
  const error = project.error || activity.error || cost.error;

  if (loading) {
    return (
      <Page>
        <LoadingState label="Loading chart analysis…" />
      </Page>
    );
  }

  if (error || !project.data) {
    return (
      <Page>
        <ErrorState
          message={
            error instanceof Error ? error.message : 'Failed to load chart analysis'
          }
        />
      </Page>
    );
  }

  const chartTitle =
    validKey === 'cost-day' && usingCalculatedCost ? 'Calculated cost / day' : def.title;
  const currency = cost.data?.currency ?? project.data.currency ?? 'USD';
  const pieData = validKey === 'cost-by-model' ? modelCostSeries : modelCalculatedSeries;

  return (
    <Page>
      <ChartDetailAnalysis
        chartKey={validKey}
        def={def}
        chartTitle={chartTitle}
        range={search.range}
        currency={currency}
        usingCalculatedCost={usingCalculatedCost}
        subtitle={search.range.label}
        breadcrumb={[
          { label: 'Projects', to: '/projects' },
          { label: project.data.name, to: `/projects/${projectId}` },
          { label: chartTitle },
        ]}
        backTo={`/projects/${projectId}`}
        backLabel="Back to project"
        idPrefix="chart-detail"
        fromDate={search.fromDate}
        toDate={search.toDate}
        rangeYear={search.rangeYear}
        rangeMonth={search.rangeMonth}
        modelFilter={search.modelFilter}
        dayFilter={search.dayFilter}
        modelOptions={modelOptions}
        dayOptions={dayOptions}
        daySeries={filteredDaySeries}
        pieData={pieData}
        barSeries={branchSeries}
        entityFilter={{
          kind: 'branch',
          label: 'Branch',
          value: search.branchFilter,
          options: branchOptions,
          paramKey: 'branch',
          emptyMessage: 'No branch activity in range.',
          searchPlaceholder: 'Search branches...',
          nameHeader: 'Branch',
          valueAsNumber: true,
        }}
        exportFilenamePrefix="project"
        onPresetChange={search.onPresetChange}
        onYearMonthChange={search.onYearMonthChange}
        updateParams={search.updateParams}
      />
    </Page>
  );
}
