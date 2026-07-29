import { useMemo } from 'react';
import { Navigate, useParams } from 'react-router-dom';
import { useAggregatedOverviewCharts } from '../api/useAggregatedOverviewCharts';
import { ChartDetailAnalysis } from '../components/ChartDetailAnalysis';
import { ErrorState, LoadingState } from '../components/States';
import {
  isOverviewChartKey,
  OVERVIEW_CHARTS,
} from '../data/overviewCharts';
import { useChartDetailSearchParams } from '../hooks/useChartDetailSearchParams';
import { Page } from '../layout/AppLayout';
import { TextLink } from '../shared/adminUi';
import {
  buildDaySeries,
  buildModelCalculatedSeries,
  buildModelCostSeries,
  resolveDisplayCost,
} from '../utils/chartDetail';
import { formatNumber } from '../utils/format';
import { toDateInputValue } from '../utils/dateRange';

export function OverviewChartDetailPage() {
  const { chartKey } = useParams();
  const search = useChartDetailSearchParams();
  const validKey = isOverviewChartKey(chartKey) ? chartKey : null;

  const {
    projectIds,
    aggregatedActivity,
    aggregatedCost,
    projectSeries,
    isLoading,
    error,
  } = useAggregatedOverviewCharts(search.range.fromUtc, search.range.toUtc);

  const { displayTotalCost, usingCalculatedCost } = resolveDisplayCost(
    aggregatedCost.totalAiCost,
    aggregatedCost.calculatedTokenCost,
  );

  const daySeries = useMemo(
    () => buildDaySeries(aggregatedActivity.byDay, displayTotalCost),
    [aggregatedActivity.byDay, displayTotalCost],
  );

  const filteredDaySeries = useMemo(() => {
    if (!search.dayFilter) return daySeries;
    return daySeries.filter((row) => row.dayKey === search.dayFilter);
  }, [daySeries, search.dayFilter]);

  const modelCostSeries = useMemo(
    () => buildModelCostSeries(aggregatedCost.byModel, search.modelFilter),
    [aggregatedCost.byModel, search.modelFilter],
  );

  const modelCalculatedSeries = useMemo(
    () => buildModelCalculatedSeries(aggregatedCost.byModel, search.modelFilter),
    [aggregatedCost.byModel, search.modelFilter],
  );

  const filteredProjectSeries = useMemo(() => {
    if (!search.projectFilter) return projectSeries;
    return projectSeries.filter((p) => p.name === search.projectFilter);
  }, [projectSeries, search.projectFilter]);

  const modelOptions = useMemo(
    () =>
      [...new Set(aggregatedCost.byModel.map((m) => m.name || 'Unknown'))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [aggregatedCost.byModel],
  );

  const projectOptions = useMemo(
    () => [...new Set(projectSeries.map((p) => p.name))].sort((a, b) => a.localeCompare(b)),
    [projectSeries],
  );

  const dayOptions = useMemo(
    () =>
      [...new Set(aggregatedActivity.byDay.map((row) => row.day))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [aggregatedActivity.byDay],
  );

  if (!validKey) {
    return <Navigate to="/" replace />;
  }

  const def = OVERVIEW_CHARTS[validKey];

  if (isLoading) {
    return (
      <Page>
        <LoadingState label="Loading chart analysis…" />
      </Page>
    );
  }

  if (error) {
    return (
      <Page>
        <ErrorState
          message={error instanceof Error ? error.message : 'Failed to load chart analysis'}
        />
      </Page>
    );
  }

  const chartTitle =
    validKey === 'cost-day' && usingCalculatedCost ? 'Calculated cost / day' : def.title;
  const currency = aggregatedCost.currency || 'USD';
  const pieData = validKey === 'cost-by-model' ? modelCostSeries : modelCalculatedSeries;
  const backQuery =
    search.range.preset === 'custom'
      ? `?range=custom&from=${encodeURIComponent(search.fromDate || toDateInputValue(search.range.fromUtc))}&to=${encodeURIComponent(search.toDate || toDateInputValue(search.range.toUtc))}`
      : `?range=${encodeURIComponent(search.range.preset)}`;

  return (
    <Page>
      <ChartDetailAnalysis
        chartKey={validKey}
        def={def}
        chartTitle={chartTitle}
        range={search.range}
        currency={currency}
        usingCalculatedCost={usingCalculatedCost}
        subtitle={
          <>
            Across {formatNumber(projectIds.length)} projects · {search.range.label}
          </>
        }
        breadcrumb={[
          { label: 'Overview', to: `/${backQuery}` },
          { label: chartTitle },
        ]}
        backTo={`/${backQuery}`}
        backLabel="Back to overview"
        idPrefix="overview-chart-detail"
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
        barSeries={filteredProjectSeries}
        entityFilter={{
          kind: 'project',
          label: 'Project',
          value: search.projectFilter,
          options: projectOptions,
          paramKey: 'project',
          emptyMessage: 'No project activity in range.',
          searchPlaceholder: 'Search projects...',
          nameHeader: 'Project',
          renderName: (row) =>
            row.projectId ? (
              <TextLink to={`/projects/${row.projectId}`}>{row.name}</TextLink>
            ) : (
              row.name
            ),
        }}
        exportFilenamePrefix="overview"
        onPresetChange={search.onPresetChange}
        onYearMonthChange={search.onYearMonthChange}
        updateParams={search.updateParams}
      />
    </Page>
  );
}
