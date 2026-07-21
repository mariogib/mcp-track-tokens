import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  useActiveSessionQuery,
  useHealthQuery,
  useReportsSummaryQuery,
  useStatusQuery,
  useUnallocatedQuery,
} from '../api/hooks';
import { useAggregatedOverviewCharts } from '../api/useAggregatedOverviewCharts';
import {
  ChartCard,
  DailyLineChart,
  NamedBarChart,
  NamedPieChart,
} from '../components/Charts';
import { DateRangeFilters } from '../components/DateRangeFilters';
import { MetricCard } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { overviewChartPath, type OverviewChartKey } from '../data/overviewCharts';
import { Page } from '../layout/AppLayout';
import {
  parseRangePreset,
  resolveRange,
  toDateInputValue,
  type RangePreset,
} from '../utils/dateRange';
import {
  formatCurrency,
  formatDateTime,
  formatDay,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
  lastDaysRange,
} from '../utils/format';

export function OverviewPage() {
  const now = new Date();
  const year = now.getUTCFullYear();
  const month = now.getUTCMonth() + 1;
  const unallocatedRange = lastDaysRange(30);

  const [searchParams, setSearchParams] = useSearchParams();
  const preset = parseRangePreset(searchParams.get('range'));
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const chartRange = useMemo(
    () =>
      resolveRange(
        preset === 'custom' || (fromDate && toDate) ? 'custom' : preset,
        fromDate,
        toDate,
      ),
    [preset, fromDate, toDate],
  );

  const health = useHealthQuery();
  const status = useStatusQuery();
  const summary = useReportsSummaryQuery(year, month);
  const session = useActiveSessionQuery();
  const unallocated = useUnallocatedQuery(unallocatedRange.fromUtc, unallocatedRange.toUtc);
  const {
    projectIds,
    aggregatedActivity,
    aggregatedCost,
    projectSeries,
    isLoading: chartsLoading,
    error: chartsError,
  } = useAggregatedOverviewCharts(chartRange.fromUtc, chartRange.toUtc);

  const updateParams = (patch: Record<string, string | null>) => {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [key, value] of Object.entries(patch)) {
          if (value == null || value === '') next.delete(key);
          else next.set(key, value);
        }
        return next;
      },
      { replace: true },
    );
  };

  const onPresetChange = (next: RangePreset) => {
    if (next === 'custom') {
      const defaults = resolveRange('30d');
      updateParams({
        range: 'custom',
        from: toDateInputValue(defaults.fromUtc),
        to: toDateInputValue(defaults.toUtc),
      });
      return;
    }
    updateParams({ range: next, from: null, to: null });
  };

  const chartLink = (key: OverviewChartKey) =>
    overviewChartPath(key, {
      range: chartRange.preset,
      from: fromDate || toDateInputValue(chartRange.fromUtc),
      to: toDate || toDateInputValue(chartRange.toUtc),
    });

  const byDayChronological = [...aggregatedActivity.byDay].sort((a, b) =>
    a.day.localeCompare(b.day),
  );

  const reportedTotalCost = aggregatedCost.totalAiCost;
  const calculatedTotalCost = aggregatedCost.calculatedTokenCost;
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const usingCalculatedCost = reportedTotalCost <= 0 && calculatedTotalCost > 0;

  const daySeries = byDayChronological.map((row) => ({
    day: formatDay(row.day),
    prompts: row.promptCount,
    activeMinutes: Math.round(row.activeProjectTimeSeconds / 60),
    agentMinutes: Math.round(row.agentDurationMilliseconds / 60000),
    tokens: row.totalTokens ?? 0,
  }));

  const tokenTotalForDays = byDayChronological.reduce(
    (sum, row) => sum + (row.totalTokens ?? 0),
    0,
  );
  const costByDay = byDayChronological.map((row) => {
    const share =
      tokenTotalForDays > 0
        ? ((row.totalTokens ?? 0) / tokenTotalForDays) * displayTotalCost
        : displayTotalCost / Math.max(byDayChronological.length, 1);
    return {
      day: formatDay(row.day),
      cost: Number(share.toFixed(2)),
    };
  });

  const modelCostSeries = aggregatedCost.byModel
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.usageBasedCost + m.subscriptionAllocation,
    }))
    .filter((m) => m.cost > 0);

  const modelCalculatedSeries = aggregatedCost.byModel
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.calculatedTokenCost ?? 0,
    }))
    .filter((m) => m.cost > 0);

  const loading = status.isLoading || summary.isLoading;
  const error = status.error || summary.error;

  if (loading) {
    return <LoadingState label="Loading overview…" />;
  }

  if (error) {
    return (
      <ErrorState
        message={
          error instanceof Error
            ? error.message
            : 'Unable to load overview. Check the API server and API key.'
        }
      />
    );
  }

  const activity = summary.data?.activity;
  const cost = summary.data?.cost;
  const unallocatedActivityCount =
    status.data?.unallocatedEventCount ?? unallocated.data?.activity?.length ?? 0;
  const unallocatedUsageCount =
    status.data?.unallocatedUsageCount ?? unallocated.data?.usage?.count ?? 0;
  const healthy =
    health.data?.healthy === true ||
    health.data?.status === 'Healthy' ||
    (health.isSuccess && !health.isError);

  return (
    <Page>
      <section className="page-section" aria-labelledby="overview-metrics">
        <div className="section-header">
          <div>
            <h2 id="overview-metrics">Today & this month</h2>
            <p>Active session context and cost signals from the local tracker.</p>
          </div>
          <StatusBadge
            label={healthy ? 'Healthy' : 'Degraded'}
            tone={healthy ? 'success' : 'danger'}
          />
        </div>

        <div className="metric-grid">
          <MetricCard
            label="Active project"
            value={status.data?.currentProject?.name ?? 'None'}
            hint={
              status.data?.activeSessionEditor
                ? `Editor: ${status.data.activeSessionEditor}`
                : 'No active editor session'
            }
          />
          <MetricCard
            label="Active session"
            value={session.data?.id ? session.data.id.slice(0, 8) : 'Idle'}
            hint={
              session.data?.startedAtUtc
                ? `Started ${formatDateTime(session.data.startedAtUtc)}`
                : 'Waiting for heartbeat'
            }
          />
          <MetricCard
            label="Prompts (month)"
            value={formatNumber(activity?.promptCount)}
            hint={`${formatNumber(activity?.agentRuns)} agent runs`}
          />
          <MetricCard
            label="Agent time"
            value={formatDurationMs(activity?.agentDurationMilliseconds)}
            hint="Sum of agent durations"
          />
          <MetricCard
            label="Active project time"
            value={formatDurationSeconds(activity?.activeProjectTimeSeconds)}
            hint="Merged activity windows"
          />
          <MetricCard
            label="Cursor cost (month)"
            value={formatCurrency(cost?.totalAiCost, cost?.currency ?? summary.data?.currency)}
            hint={`Usage ${formatCurrency(cost?.usageBasedCost, cost?.currency)} · Sub ${formatCurrency(cost?.subscriptionAllocation, cost?.currency)} · Token ${formatCurrency(cost?.calculatedTokenCost ?? 0, cost?.currency)}`}
          />
          <MetricCard
            label="Unallocated activity"
            value={formatNumber(unallocatedActivityCount)}
            hint="Click to assign events to projects"
            to="/unallocated"
          />
          <MetricCard
            label="Unallocated usage"
            value={formatNumber(unallocatedUsageCount)}
            hint={formatCurrency(cost?.unallocatedCost, cost?.currency)}
            to="/imported-usage"
          />
        </div>
      </section>

      <section className="page-section" aria-labelledby="overview-all-projects">
        <div className="section-header">
          <div>
            <h2 id="overview-all-projects">Across all projects</h2>
            <p>
              Same overview charts as project details, aggregated for the selected range
              ({chartRange.label}).
            </p>
          </div>
        </div>

        <DateRangeFilters
          preset={chartRange.preset}
          fromDate={fromDate || toDateInputValue(chartRange.fromUtc)}
          toDate={toDate || toDateInputValue(chartRange.toUtc)}
          onPresetChange={onPresetChange}
          onFromDateChange={(value) =>
            updateParams({
              range: 'custom',
              from: value,
              to: toDate || toDateInputValue(chartRange.toUtc),
            })
          }
          onToDateChange={(value) =>
            updateParams({
              range: 'custom',
              from: fromDate || toDateInputValue(chartRange.fromUtc),
              to: value,
            })
          }
          idPrefix="overview-range"
        />

        {chartsLoading ? (
          <LoadingState label="Loading project charts…" />
        ) : chartsError ? (
          <ErrorState
            message={
              chartsError instanceof Error
                ? chartsError.message
                : 'Unable to load project activity and cost charts.'
            }
          />
        ) : projectIds.length === 0 ? (
          <EmptyState message="No projects yet — register a project to see overview charts." />
        ) : (
          <>
            <div className="metric-grid">
              <MetricCard
                label="Prompts"
                value={formatNumber(aggregatedActivity.promptCount)}
              />
              <MetricCard
                label="Agent time"
                value={formatDurationMs(aggregatedActivity.agentDurationMilliseconds)}
              />
              <MetricCard
                label="Active time"
                value={formatDurationSeconds(aggregatedActivity.activeProjectTimeSeconds)}
              />
              <MetricCard
                label="Total tokens"
                value={formatNumber(aggregatedCost.importedTotalTokens)}
              />
              <MetricCard
                label={usingCalculatedCost ? 'Calculated token cost' : 'Total AI cost'}
                value={formatCurrency(displayTotalCost, aggregatedCost.currency)}
                hint={
                  usingCalculatedCost
                    ? 'Reported usage cost is $0 — showing Settings rate card × tokens'
                    : `${formatNumber(projectIds.length)} projects`
                }
              />
            </div>

            <div className="chart-grid">
              <ChartCard title="Prompts / day" to={chartLink('prompts-day')}>
                <DailyLineChart data={daySeries} xKey="day" yKey="prompts" yLabel="Prompts" />
              </ChartCard>
              <ChartCard title="Active time / day (minutes)" to={chartLink('active-time-day')}>
                <DailyLineChart
                  data={daySeries}
                  xKey="day"
                  yKey="activeMinutes"
                  yLabel="Minutes"
                />
              </ChartCard>
              <ChartCard
                title="Agent duration / day (minutes)"
                to={chartLink('agent-duration-day')}
              >
                <DailyLineChart
                  data={daySeries}
                  xKey="day"
                  yKey="agentMinutes"
                  yLabel="Minutes"
                />
              </ChartCard>
              <ChartCard
                title={usingCalculatedCost ? 'Calculated cost / day' : 'Cost / day'}
                to={chartLink('cost-day')}
              >
                <DailyLineChart data={costByDay} xKey="day" yKey="cost" yLabel="Cost" />
              </ChartCard>
              <ChartCard title="Tokens / day" to={chartLink('tokens-day')}>
                <DailyLineChart data={daySeries} xKey="day" yKey="tokens" yLabel="Tokens" />
              </ChartCard>
              <ChartCard title="Cost by model" to={chartLink('cost-by-model')}>
                {modelCostSeries.length ? (
                  <NamedPieChart data={modelCostSeries} valueKey="cost" />
                ) : (
                  <EmptyState message="No reported model cost in range (usage/subscription)." />
                )}
              </ChartCard>
              <ChartCard
                title="Calculated cost by model"
                to={chartLink('calculated-cost-by-model')}
              >
                {modelCalculatedSeries.length ? (
                  <NamedPieChart data={modelCalculatedSeries} valueKey="cost" />
                ) : (
                  <EmptyState message="No calculated token cost in range." />
                )}
              </ChartCard>
              <ChartCard title="Activity by project" to={chartLink('activity-by-project')}>
                {projectSeries.length ? (
                  <NamedBarChart
                    data={projectSeries}
                    valueKey="prompts"
                    valueLabel="Prompts"
                  />
                ) : (
                  <EmptyState message="No project activity in range." />
                )}
              </ChartCard>
            </div>
          </>
        )}
      </section>

      <section className="page-section" aria-labelledby="overview-health">
        <div className="section-header">
          <div>
            <h2 id="overview-health">Server health</h2>
            <p>Database path, queue depth, and latest ingest.</p>
          </div>
        </div>
        <div className="panel stack">
          <div className="row">
            <StatusBadge
              label={status.data?.isHealthy ? 'Tracker OK' : 'Tracker issue'}
              tone={status.data?.isHealthy ? 'success' : 'warning'}
            />
            <span className="mono">{status.data?.databasePath ?? '—'}</span>
          </div>
          <div className="field-row">
            <div>
              <div className="label">Provider</div>
              <strong>{status.data?.databaseProvider ?? '—'}</strong>
            </div>
            <div>
              <div className="label">Queued events</div>
              <strong>{formatNumber(status.data?.queuedEventCount)}</strong>
            </div>
            <div>
              <div className="label">Last event</div>
              <strong>{formatDateTime(status.data?.lastEventAtUtc)}</strong>
            </div>
            <div>
              <div className="label">Last Cursor import</div>
              <strong>
                {formatDateTime(status.data?.lastCursorImportAtUtc)}{' '}
                {status.data?.lastCursorImportStatus
                  ? `(${status.data.lastCursorImportStatus})`
                  : ''}
              </strong>
            </div>
          </div>
        </div>
      </section>
    </Page>
  );
}
