import { useMemo } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import {
  useProjectActivityQuery,
  useProjectCostQuery,
  useProjectQuery,
} from '../api/hooks';
import {
  ChartCard,
  DailyLineChart,
  NamedBarChart,
  NamedPieChart,
} from '../components/Charts';
import { DateRangeFilters } from '../components/DateRangeFilters';
import { AnalysisDetailBrowse } from '../components/AnalysisDetailBrowse';
import { MetricCard, Panel } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import {
  isProjectChartKey,
  PROJECT_CHARTS,
  type ProjectChartKey,
} from '../data/projectCharts';
import { Page } from '../layout/AppLayout';
import { Breadcrumb, TextLink } from '../shared/adminUi';
import {
  currentUtcYearMonth,
  parseMonthParam,
  parseRangePreset,
  parseYearParam,
  resolveRange,
  toDateInputValue,
  type RangePreset,
} from '../utils/dateRange';
import {
  formatCurrency,
  formatDay,
  formatNumber,
  millisecondsToMinutes,
  millisecondsToMinutesExact,
} from '../utils/format';

type SeriesPoint = Record<string, string | number>;

export function ProjectChartDetailPage() {
  const { projectId, chartKey } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();

  if (!projectId || !isProjectChartKey(chartKey)) {
    return <Navigate to={projectId ? `/projects/${projectId}` : '/projects'} replace />;
  }

  const def = PROJECT_CHARTS[chartKey];
  const preset = parseRangePreset(searchParams.get('range'));
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const rangeYear = parseYearParam(searchParams.get('year'));
  const rangeMonth = parseMonthParam(searchParams.get('month'));
  const modelFilter = searchParams.get('model') ?? '';
  const branchFilter = searchParams.get('branch') ?? '';
  const dayFilter = searchParams.get('day') ?? '';

  const range = useMemo(
    () =>
      resolveRange(
        preset === 'custom' || (fromDate && toDate) ? 'custom' : preset,
        fromDate,
        toDate,
        rangeYear,
        rangeMonth,
      ),
    [preset, fromDate, toDate, rangeYear, rangeMonth],
  );

  const project = useProjectQuery(projectId);
  const activity = useProjectActivityQuery(projectId, range.fromUtc, range.toUtc);
  const cost = useProjectCostQuery(projectId, range.fromUtc, range.toUtc);

  const updateParams = (patch: Record<string, string | null>) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      for (const [key, value] of Object.entries(patch)) {
        if (value == null || value === '') next.delete(key);
        else next.set(key, value);
      }
      return next;
    }, { replace: true });
  };

  const onPresetChange = (next: RangePreset) => {
    if (next === 'custom') {
      const defaults = resolveRange('30d');
      updateParams({
        range: 'custom',
        from: toDateInputValue(defaults.fromUtc),
        to: toDateInputValue(defaults.toUtc),
        year: null,
        month: null,
      });
      return;
    }
    if (next === 'month') {
      const defaults = currentUtcYearMonth();
      updateParams({
        range: 'month',
        year: String(defaults.year),
        month: String(defaults.month),
        from: null,
        to: null,
      });
      return;
    }
    updateParams({ range: next, from: null, to: null, year: null, month: null });
  };

  const onYearMonthChange = (nextYear: number, nextMonth: number) => {
    updateParams({
      range: 'month',
      year: String(nextYear),
      month: String(nextMonth),
      from: null,
      to: null,
    });
  };

  const reportedTotalCost = cost.data?.totalAiCost ?? 0;
  const calculatedTotalCost = cost.data?.calculatedTokenCost ?? 0;
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const usingCalculatedCost = reportedTotalCost <= 0 && calculatedTotalCost > 0;

  const byDayChronological = useMemo(() => {
    const rows = [...(activity.data?.byDay ?? [])];
    rows.sort((a, b) => a.day.localeCompare(b.day));
    return rows;
  }, [activity.data?.byDay]);

  const daySeries = useMemo(() => {
    const tokenTotal = byDayChronological.reduce((sum, row) => sum + (row.totalTokens ?? 0), 0);
    return byDayChronological.map((row) => {
      const costShare =
        tokenTotal > 0
          ? ((row.totalTokens ?? 0) / tokenTotal) * displayTotalCost
          : displayTotalCost / Math.max(byDayChronological.length, 1);
      return {
        dayKey: row.day,
        day: formatDay(row.day),
        prompts: row.promptCount,
        activeMinutes: Math.round(row.activeProjectTimeSeconds / 60),
        agentDurationMilliseconds: row.agentDurationMilliseconds,
        agentMinutes: millisecondsToMinutesExact(row.agentDurationMilliseconds),
        tokens: row.totalTokens ?? 0,
        cost: Number(costShare.toFixed(4)),
      };
    });
  }, [byDayChronological, displayTotalCost]);

  const filteredDaySeries = useMemo(() => {
    if (!dayFilter) return daySeries;
    return daySeries.filter((row) => row.dayKey === dayFilter);
  }, [daySeries, dayFilter]);

  const modelCostSeries = useMemo(() => {
    const rows = (cost.data?.byModel ?? [])
      .map((m) => ({
        name: m.name || 'Unknown',
        cost: m.usageBasedCost + m.subscriptionAllocation,
      }))
      .filter((m) => m.cost > 0);
    if (!modelFilter) return rows;
    return rows.filter((m) => m.name === modelFilter);
  }, [cost.data?.byModel, modelFilter]);

  const modelCalculatedSeries = useMemo(() => {
    const rows = (cost.data?.byModel ?? [])
      .map((m) => ({
        name: m.name || 'Unknown',
        cost: m.calculatedTokenCost ?? 0,
      }))
      .filter((m) => m.cost > 0);
    if (!modelFilter) return rows;
    return rows.filter((m) => m.name === modelFilter);
  }, [cost.data?.byModel, modelFilter]);

  const branchSeries = useMemo(() => {
    const rows = (activity.data?.byBranch ?? []).map((b) => ({
      name: b.name || '(none)',
      prompts: b.promptCount,
    }));
    if (!branchFilter) return rows;
    return rows.filter((b) => b.name === branchFilter);
  }, [activity.data?.byBranch, branchFilter]);

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
    () => byDayChronological.map((row) => row.day),
    [byDayChronological],
  );

  const chartTitle =
    chartKey === 'cost-day' && usingCalculatedCost ? 'Calculated cost / day' : def.title;

  const lineYKey = lineValueKey(chartKey);
  const activeLineData = filteredDaySeries;
  const pieData = chartKey === 'cost-by-model' ? modelCostSeries : modelCalculatedSeries;

  const stats = useMemo(() => computeStats(chartKey, {
    daySeries: activeLineData,
    pieData,
    branchSeries,
  }), [chartKey, activeLineData, pieData, branchSeries]);

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

  const currency = cost.data?.currency ?? project.data.currency ?? 'USD';

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <Breadcrumb
              items={[
                { label: 'Projects', to: '/projects' },
                { label: project.data.name, to: `/projects/${projectId}` },
                { label: chartTitle },
              ]}
            />
            <h2>{chartTitle}</h2>
            <p className="muted">
              {range.label}
              {usingCalculatedCost && chartKey.includes('cost')
                ? ' · reported usage cost is $0 — using calculated token cost'
                : ''}
            </p>
          </div>
          <TextLink to={`/projects/${projectId}`} variant="muted">
            Back to project
          </TextLink>
        </div>

        <Panel className="stack">
          <DateRangeFilters
            idPrefix="chart-detail"
            preset={range.preset}
            fromDate={fromDate || toDateInputValue(range.fromUtc)}
            toDate={toDate || toDateInputValue(range.toUtc)}
            onPresetChange={onPresetChange}
            onFromDateChange={(value) =>
              updateParams({
                range: 'custom',
                from: value,
                to: toDate || toDateInputValue(range.toUtc),
                year: null,
                month: null,
              })
            }
            onToDateChange={(value) =>
              updateParams({
                range: 'custom',
                to: value,
                from: fromDate || toDateInputValue(range.fromUtc),
                year: null,
                month: null,
              })
            }
            year={rangeYear ?? currentUtcYearMonth().year}
            month={rangeMonth ?? currentUtcYearMonth().month}
            onYearMonthChange={onYearMonthChange}
          />

          {def.filter === 'model' ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor="chart-model-filter">Model</label>
                <select
                  id="chart-model-filter"
                  value={modelFilter}
                  onChange={(e) => updateParams({ model: e.target.value || null })}
                >
                  <option value="">All models</option>
                  {modelOptions.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          ) : null}

          {def.filter === 'branch' ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor="chart-branch-filter">Branch</label>
                <select
                  id="chart-branch-filter"
                  value={branchFilter}
                  onChange={(e) => updateParams({ branch: e.target.value || null })}
                >
                  <option value="">All branches</option>
                  {branchOptions.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          ) : null}

          {def.filter === 'day' ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor="chart-day-filter">Day</label>
                <select
                  id="chart-day-filter"
                  value={dayFilter}
                  onChange={(e) => updateParams({ day: e.target.value || null })}
                >
                  <option value="">All days</option>
                  {dayOptions.map((day) => (
                    <option key={day} value={day}>
                      {formatDay(day)} ({day})
                    </option>
                  ))}
                </select>
              </div>
            </div>
          ) : null}
        </Panel>
      </section>

      <section className="page-section">
        <div className="metric-grid">
          <MetricCard label="Total" value={formatStat(stats.total, chartKey, currency)} />
          <MetricCard label="Average" value={formatStat(stats.avg, chartKey, currency)} />
          <MetricCard label="Max" value={formatStat(stats.max, chartKey, currency)} />
          <MetricCard label="Points" value={formatNumber(stats.count)} />
        </div>

        <ChartCard title={chartTitle} height={360}>
          {def.kind === 'line' ? (
            activeLineData.length ? (
              <DailyLineChart
                data={activeLineData}
                xKey="day"
                yKey={lineYKey}
                yLabel={def.yLabel}
                onPointClick={(point) => {
                  const key = String(point.dayKey ?? '');
                  if (key) updateParams({ day: key });
                }}
              />
            ) : (
              <EmptyState message="No daily data in this range." />
            )
          ) : null}
          {def.kind === 'pie' ? (
            pieData.length ? (
              <NamedPieChart
                data={pieData}
                valueKey="cost"
                onItemClick={(name) => updateParams({ model: name })}
              />
            ) : (
              <EmptyState
                message={
                  chartKey === 'cost-by-model'
                    ? 'No reported model cost in range (usage/subscription).'
                    : 'No calculated token cost in range.'
                }
              />
            )
          ) : null}
          {def.kind === 'bar' ? (
            branchSeries.length ? (
              <NamedBarChart
                data={branchSeries}
                valueKey="prompts"
                valueLabel={def.valueLabel}
                onItemClick={(name) => updateParams({ branch: name })}
              />
            ) : (
              <EmptyState message="No branch activity in range." />
            )
          ) : null}
        </ChartCard>
      </section>

      {def.kind === 'line' ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder="Search days..."
          rows={activeLineData}
          getSearchText={(row) =>
            [row.day, row.dayKey, row.prompts, row.activeMinutes, row.agentMinutes, row.tokens, row.cost]
              .map(String)
              .join(' ')
          }
          exportFilename={`project-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={[
            { header: 'Day', key: 'day' },
            { header: 'Prompts', key: 'prompts' },
            { header: 'Active (min)', key: 'activeMinutes' },
            { header: 'Agent (min)', key: 'agentMinutes' },
            { header: 'Tokens', key: 'tokens' },
            { header: 'Cost', key: 'cost' },
          ]}
          toExportRow={(row) => ({
            day: String(row.day),
            prompts: Number(row.prompts),
            activeMinutes: Number(row.activeMinutes),
            agentMinutes: millisecondsToMinutes(Number(row.agentDurationMilliseconds ?? 0)),
            tokens: Number(row.tokens),
            cost: Number(row.cost),
          })}
          renderTable={(rows) => <DayTable rows={rows} chartKey={chartKey} currency={currency} />}
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={String(row.dayKey)} className="analysis-browse-tile">
                <strong>{String(row.day)}</strong>
                <span>Prompts {formatNumber(Number(row.prompts))}</span>
                <span>Active {formatNumber(Number(row.activeMinutes))} min</span>
                <span>
                  Agent{' '}
                  {formatNumber(
                    millisecondsToMinutes(Number(row.agentDurationMilliseconds ?? 0)),
                  )}{' '}
                  min
                </span>
                <span>Tokens {formatNumber(Number(row.tokens))}</span>
                <span>
                  {chartKey === 'cost-day' || Number(row.cost) > 0
                    ? formatCurrency(Number(row.cost), currency)
                    : '—'}
                </span>
              </article>
            ))
          }
        />
      ) : null}

      {def.kind === 'pie' ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder="Search models..."
          rows={pieData}
          getSearchText={(row) => `${row.name} ${row.cost}`}
          exportFilename={`project-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={[
            { header: 'Model', key: 'name' },
            {
              header: chartKey === 'calculated-cost-by-model' ? 'Calculated cost' : 'Cost',
              key: 'cost',
            },
          ]}
          toExportRow={(row) => ({ name: row.name, cost: row.cost })}
          renderTable={(rows) => (
            <NamedValueTable
              rows={rows}
              nameHeader="Model"
              valueHeader={chartKey === 'calculated-cost-by-model' ? 'Calculated cost' : 'Cost'}
              currency={currency}
            />
          )}
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.name} className="analysis-browse-tile">
                <strong>{row.name}</strong>
                <span>{formatCurrency(row.cost, currency)}</span>
              </article>
            ))
          }
        />
      ) : null}

      {def.kind === 'bar' ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder="Search branches..."
          rows={branchSeries}
          getSearchText={(row) => `${row.name} ${row.prompts}`}
          exportFilename={`project-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={[
            { header: 'Branch', key: 'name' },
            { header: 'Prompts', key: 'prompts' },
          ]}
          toExportRow={(row) => ({ name: row.name, prompts: row.prompts })}
          renderTable={(rows) => (
            <NamedValueTable
              rows={rows.map((r) => ({ name: r.name, cost: r.prompts }))}
              nameHeader="Branch"
              valueHeader="Prompts"
              currency={currency}
              asNumber
            />
          )}
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.name} className="analysis-browse-tile">
                <strong>{row.name}</strong>
                <span>Prompts {formatNumber(row.prompts)}</span>
              </article>
            ))
          }
        />
      ) : null}
    </Page>
  );
}

function lineValueKey(chartKey: ProjectChartKey): string {
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

function computeStats(
  chartKey: ProjectChartKey,
  data: {
    daySeries: SeriesPoint[];
    pieData: Array<{ name: string; cost: number }>;
    branchSeries: Array<{ name: string; prompts: number }>;
  },
) {
  if (chartKey === 'cost-by-model' || chartKey === 'calculated-cost-by-model') {
    const values = data.pieData.map((r) => r.cost);
    return summarize(values);
  }
  if (chartKey === 'activity-by-branch') {
    const values = data.branchSeries.map((r) => r.prompts);
    return summarize(values);
  }
  if (chartKey === 'agent-duration-day') {
    const msValues = data.daySeries.map((r) => Number(r.agentDurationMilliseconds ?? 0));
    if (!msValues.length) {
      return { total: 0, avg: 0, max: 0, count: 0 };
    }
    const totalMs = msValues.reduce((s, v) => s + v, 0);
    return {
      total: millisecondsToMinutesExact(totalMs),
      avg: millisecondsToMinutesExact(totalMs / msValues.length),
      max: millisecondsToMinutesExact(Math.max(...msValues)),
      count: msValues.length,
    };
  }
  const key = lineValueKey(chartKey);
  const values = data.daySeries.map((r) => Number(r[key] ?? 0));
  return summarize(values);
}

function summarize(values: number[]) {
  if (!values.length) {
    return { total: 0, avg: 0, max: 0, count: 0 };
  }
  const total = values.reduce((s, v) => s + v, 0);
  return {
    total,
    avg: total / values.length,
    max: Math.max(...values),
    count: values.length,
  };
}

function formatStat(value: number, chartKey: ProjectChartKey, currency: string): string {
  if (
    chartKey === 'cost-day' ||
    chartKey === 'cost-by-model' ||
    chartKey === 'calculated-cost-by-model'
  ) {
    return formatCurrency(value, currency);
  }
  if (chartKey === 'tokens-day') {
    return formatNumber(Math.round(value));
  }
  if (value % 1 !== 0) {
    return formatNumber(Number(value.toFixed(1)));
  }
  return formatNumber(value);
}

function DayTable({
  rows,
  chartKey,
  currency,
}: {
  rows: SeriesPoint[];
  chartKey: ProjectChartKey;
  currency: string;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>Day</th>
          <th>Prompts</th>
          <th>Active (min)</th>
          <th>Agent (min)</th>
          <th>Tokens</th>
          <th>Cost</th>
        </tr>
      </thead>
      <tbody>
        {rows.length ? (
          rows.map((row) => (
            <tr key={String(row.dayKey)}>
              <td>{String(row.day)}</td>
              <td>{formatNumber(Number(row.prompts))}</td>
              <td>{formatNumber(Number(row.activeMinutes))}</td>
              <td>
                {formatNumber(
                  millisecondsToMinutes(Number(row.agentDurationMilliseconds ?? 0)),
                )}
              </td>
              <td>{formatNumber(Number(row.tokens))}</td>
              <td>
                {chartKey === 'cost-day' || Number(row.cost) > 0
                  ? formatCurrency(Number(row.cost), currency)
                  : '—'}
              </td>
            </tr>
          ))
        ) : (
          <tr>
            <td colSpan={6}>No rows</td>
          </tr>
        )}
      </tbody>
    </table>
  );
}

function NamedValueTable({
  rows,
  nameHeader,
  valueHeader,
  currency,
  asNumber = false,
}: {
  rows: Array<{ name: string; cost: number }>;
  nameHeader: string;
  valueHeader: string;
  currency: string;
  asNumber?: boolean;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>{nameHeader}</th>
          <th>{valueHeader}</th>
        </tr>
      </thead>
      <tbody>
        {rows.length ? (
          rows.map((row) => (
            <tr key={row.name}>
              <td>{row.name}</td>
              <td>
                {asNumber ? formatNumber(row.cost) : formatCurrency(row.cost, currency)}
              </td>
            </tr>
          ))
        ) : (
          <tr>
            <td colSpan={2}>No rows</td>
          </tr>
        )}
      </tbody>
    </table>
  );
}
