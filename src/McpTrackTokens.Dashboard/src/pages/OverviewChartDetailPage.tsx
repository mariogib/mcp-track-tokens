import { useMemo } from 'react';
import { useQueries } from '@tanstack/react-query';
import { Navigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { queryKeys } from '../api/hooks';
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
  enrichModelCostWithTokenRates,
  resolveDisplayCost,
} from '../utils/chartDetail';
import { formatNumber } from '../utils/format';
import { toDateInputValue } from '../utils/dateRange';
import type { TokenCostModelRow } from '../api/types';

function mergeTokenCostModels(rows: TokenCostModelRow[]): TokenCostModelRow[] {
  const byKey = new Map<string, TokenCostModelRow>();
  for (const row of rows) {
    const key = (row.model || 'Unknown').trim().toLowerCase() || 'unknown';
    const existing = byKey.get(key);
    if (!existing) {
      byKey.set(key, { ...row });
      continue;
    }
    byKey.set(key, {
      ...existing,
      inputTokens: existing.inputTokens + row.inputTokens,
      outputTokens: existing.outputTokens + row.outputTokens,
      cachedInputTokens: existing.cachedInputTokens + row.cachedInputTokens,
      cacheWriteTokens: (existing.cacheWriteTokens ?? 0) + (row.cacheWriteTokens ?? 0),
      reasoningTokens: existing.reasoningTokens + row.reasoningTokens,
      totalTokens: existing.totalTokens + row.totalTokens,
      estimatedCost: existing.estimatedCost + row.estimatedCost,
      reportedCost: existing.reportedCost + row.reportedCost,
      // Keep the first non-empty rate card values for display.
      rateSource: existing.rateSource || row.rateSource,
      inputPerMillion: existing.inputPerMillion || row.inputPerMillion,
      outputPerMillion: existing.outputPerMillion || row.outputPerMillion,
      cacheReadPerMillion: existing.cacheReadPerMillion || row.cacheReadPerMillion,
      cacheWritePerMillion:
        (existing.cacheWritePerMillion ?? 0) || (row.cacheWritePerMillion ?? 0),
    });
  }
  return [...byKey.values()];
}

export function OverviewChartDetailPage() {
  const { chartKey } = useParams();
  const search = useChartDetailSearchParams();
  const validKey = isOverviewChartKey(chartKey) ? chartKey : null;
  const needsTokenRates = validKey === 'calculated-cost-by-model';

  const {
    projectIds,
    aggregatedActivity,
    aggregatedCost,
    projectSeries,
    isLoading,
    error,
  } = useAggregatedOverviewCharts(search.range.fromUtc, search.range.toUtc);

  const tokenCostQueries = useQueries({
    queries: projectIds.map((id) => ({
      queryKey: queryKeys.projectTokenCost(id, search.range.fromUtc, search.range.toUtc),
      queryFn: ({ signal }: { signal?: AbortSignal }) =>
        api.getProjectTokenCost(id, search.range.fromUtc, search.range.toUtc, signal),
      enabled: needsTokenRates && projectIds.length > 0,
    })),
  });

  const tokenCostByModel = tokenCostQueries.flatMap((q) => q.data?.byModel ?? []);
  const tokenCostDataKey = tokenCostQueries.map((q) => q.dataUpdatedAt).join('|');

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
    () =>
      enrichModelCostWithTokenRates(
        buildModelCalculatedSeries(aggregatedCost.byModel, search.modelFilter),
        mergeTokenCostModels(tokenCostByModel),
      ),
    // tokenCostDataKey tracks when any project token-cost query updates.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [aggregatedCost.byModel, search.modelFilter, tokenCostDataKey],
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
  const tokenRatesLoading =
    needsTokenRates && projectIds.length > 0 && tokenCostQueries.some((q) => q.isLoading);
  const tokenRatesError = needsTokenRates
    ? tokenCostQueries.find((q) => q.error)?.error ?? null
    : null;

  if (isLoading || tokenRatesLoading) {
    return (
      <Page>
        <LoadingState label="Loading chart analysis…" />
      </Page>
    );
  }

  if (error || tokenRatesError) {
    return (
      <Page>
        <ErrorState
          message={
            (error instanceof Error
              ? error.message
              : null) ??
            (tokenRatesError instanceof Error
              ? tokenRatesError.message
              : null) ??
            'Failed to load chart analysis'
          }
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
