import { useMemo } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import { useAggregatedOverviewCharts } from '../api/useAggregatedOverviewCharts';
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
  isOverviewChartKey,
  OVERVIEW_CHARTS,
  type OverviewChartKey,
} from '../data/overviewCharts';
import { Page } from '../layout/AppLayout';
import { Breadcrumb, TextLink } from '../shared/adminUi';
import {
  parseRangePreset,
  resolveRange,
  toDateInputValue,
  type RangePreset,
} from '../utils/dateRange';
import {
  formatCurrency,
  formatDay,
  formatNumber,
} from '../utils/format';

type SeriesPoint = Record<string, string | number>;

export function OverviewChartDetailPage() {
  const { chartKey } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();

  if (!isOverviewChartKey(chartKey)) {
    return <Navigate to="/" replace />;
  }

  const def = OVERVIEW_CHARTS[chartKey];
  const preset = parseRangePreset(searchParams.get('range'));
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const modelFilter = searchParams.get('model') ?? '';
  const projectFilter = searchParams.get('project') ?? '';
  const dayFilter = searchParams.get('day') ?? '';

  const range = useMemo(
    () =>
      resolveRange(
        preset === 'custom' || (fromDate && toDate) ? 'custom' : preset,
        fromDate,
        toDate,
      ),
    [preset, fromDate, toDate],
  );

  const {
    projectIds,
    aggregatedActivity,
    aggregatedCost,
    projectSeries,
    isLoading,
    error,
  } = useAggregatedOverviewCharts(range.fromUtc, range.toUtc);

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
      });
      return;
    }
    updateParams({ range: next, from: null, to: null });
  };

  const reportedTotalCost = aggregatedCost.totalAiCost;
  const calculatedTotalCost = aggregatedCost.calculatedTokenCost;
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const usingCalculatedCost = reportedTotalCost <= 0 && calculatedTotalCost > 0;

  const byDayChronological = useMemo(() => {
    const rows = [...aggregatedActivity.byDay];
    rows.sort((a, b) => a.day.localeCompare(b.day));
    return rows;
  }, [aggregatedActivity.byDay]);

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
        agentMinutes: Math.round(row.agentDurationMilliseconds / 60000),
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
    const rows = aggregatedCost.byModel
      .map((m) => ({
        name: m.name || 'Unknown',
        cost: m.usageBasedCost + m.subscriptionAllocation,
      }))
      .filter((m) => m.cost > 0);
    if (!modelFilter) return rows;
    return rows.filter((m) => m.name === modelFilter);
  }, [aggregatedCost.byModel, modelFilter]);

  const modelCalculatedSeries = useMemo(() => {
    const rows = aggregatedCost.byModel
      .map((m) => ({
        name: m.name || 'Unknown',
        cost: m.calculatedTokenCost ?? 0,
      }))
      .filter((m) => m.cost > 0);
    if (!modelFilter) return rows;
    return rows.filter((m) => m.name === modelFilter);
  }, [aggregatedCost.byModel, modelFilter]);

  const filteredProjectSeries = useMemo(() => {
    if (!projectFilter) return projectSeries;
    return projectSeries.filter((p) => p.name === projectFilter);
  }, [projectSeries, projectFilter]);

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
    () => byDayChronological.map((row) => row.day),
    [byDayChronological],
  );

  const chartTitle =
    chartKey === 'cost-day' && usingCalculatedCost ? 'Calculated cost / day' : def.title;

  const lineYKey = lineValueKey(chartKey);
  const pieData = chartKey === 'cost-by-model' ? modelCostSeries : modelCalculatedSeries;

  const stats = useMemo(
    () =>
      computeStats(chartKey, {
        daySeries: filteredDaySeries,
        pieData,
        projectSeries: filteredProjectSeries,
      }),
    [chartKey, filteredDaySeries, pieData, filteredProjectSeries],
  );

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

  const currency = aggregatedCost.currency || 'USD';
  const backQuery =
    range.preset === 'custom'
      ? `?range=custom&from=${encodeURIComponent(fromDate || toDateInputValue(range.fromUtc))}&to=${encodeURIComponent(toDate || toDateInputValue(range.toUtc))}`
      : `?range=${encodeURIComponent(range.preset)}`;

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <Breadcrumb
              items={[
                { label: 'Overview', to: `/${backQuery}` },
                { label: chartTitle },
              ]}
            />
            <h2>{chartTitle}</h2>
            <p className="muted">
              Across {formatNumber(projectIds.length)} projects · {range.label}
              {usingCalculatedCost && chartKey.includes('cost')
                ? ' · reported usage cost is $0 — using calculated token cost'
                : ''}
            </p>
          </div>
          <TextLink to={`/${backQuery}`} variant="muted">
            Back to overview
          </TextLink>
        </div>

        <Panel className="stack">
          <DateRangeFilters
            idPrefix="overview-chart-detail"
            preset={range.preset}
            fromDate={fromDate || toDateInputValue(range.fromUtc)}
            toDate={toDate || toDateInputValue(range.toUtc)}
            onPresetChange={onPresetChange}
            onFromDateChange={(value) =>
              updateParams({
                range: 'custom',
                from: value,
                to: toDate || toDateInputValue(range.toUtc),
              })
            }
            onToDateChange={(value) =>
              updateParams({
                range: 'custom',
                to: value,
                from: fromDate || toDateInputValue(range.fromUtc),
              })
            }
          />

          {def.filter === 'model' ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor="overview-chart-model-filter">Model</label>
                <select
                  id="overview-chart-model-filter"
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

          {def.filter === 'project' ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor="overview-chart-project-filter">Project</label>
                <select
                  id="overview-chart-project-filter"
                  value={projectFilter}
                  onChange={(e) => updateParams({ project: e.target.value || null })}
                >
                  <option value="">All projects</option>
                  {projectOptions.map((name) => (
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
                <label htmlFor="overview-chart-day-filter">Day</label>
                <select
                  id="overview-chart-day-filter"
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
            filteredDaySeries.length ? (
              <DailyLineChart
                data={filteredDaySeries}
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
            filteredProjectSeries.length ? (
              <NamedBarChart
                data={filteredProjectSeries}
                valueKey="prompts"
                valueLabel={def.valueLabel}
                onItemClick={(name) => updateParams({ project: name })}
              />
            ) : (
              <EmptyState message="No project activity in range." />
            )
          ) : null}
        </ChartCard>
      </section>

      {def.kind === 'line' ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder="Search days..."
          rows={filteredDaySeries}
          getSearchText={(row) =>
            [row.day, row.dayKey, row.prompts, row.activeMinutes, row.agentMinutes, row.tokens, row.cost]
              .map(String)
              .join(' ')
          }
          exportFilename={`overview-${chartKey}-detail.xlsx`}
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
            agentMinutes: Number(row.agentMinutes),
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
          exportFilename={`overview-${chartKey}-detail.xlsx`}
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
          searchPlaceholder="Search projects..."
          rows={filteredProjectSeries}
          getSearchText={(row) => `${row.name} ${row.prompts}`}
          exportFilename={`overview-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={[
            { header: 'Project', key: 'name' },
            { header: 'Prompts', key: 'prompts' },
          ]}
          toExportRow={(row) => ({ name: row.name, prompts: row.prompts })}
          renderTable={(rows) => <ProjectTable rows={rows} />}
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.projectId || row.name} className="analysis-browse-tile">
                <strong>
                  {row.projectId ? (
                    <TextLink to={`/projects/${row.projectId}`}>{row.name}</TextLink>
                  ) : (
                    row.name
                  )}
                </strong>
                <span>Prompts {formatNumber(row.prompts)}</span>
              </article>
            ))
          }
        />
      ) : null}
    </Page>
  );
}

function lineValueKey(chartKey: OverviewChartKey): string {
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
  chartKey: OverviewChartKey,
  data: {
    daySeries: SeriesPoint[];
    pieData: Array<{ name: string; cost: number }>;
    projectSeries: Array<{ name: string; prompts: number }>;
  },
) {
  if (chartKey === 'cost-by-model' || chartKey === 'calculated-cost-by-model') {
    return summarize(data.pieData.map((r) => r.cost));
  }
  if (chartKey === 'activity-by-project') {
    return summarize(data.projectSeries.map((r) => r.prompts));
  }
  const key = lineValueKey(chartKey);
  return summarize(data.daySeries.map((r) => Number(r[key] ?? 0)));
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

function formatStat(value: number, chartKey: OverviewChartKey, currency: string): string {
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
  chartKey: OverviewChartKey;
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
              <td>{formatNumber(Number(row.agentMinutes))}</td>
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
}: {
  rows: Array<{ name: string; cost: number }>;
  nameHeader: string;
  valueHeader: string;
  currency: string;
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
              <td>{formatCurrency(row.cost, currency)}</td>
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

function ProjectTable({
  rows,
}: {
  rows: Array<{ projectId: string; name: string; prompts: number }>;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>Project</th>
          <th>Prompts</th>
        </tr>
      </thead>
      <tbody>
        {rows.length ? (
          rows.map((row) => (
            <tr key={row.projectId || row.name}>
              <td>
                {row.projectId ? (
                  <TextLink to={`/projects/${row.projectId}`}>{row.name}</TextLink>
                ) : (
                  row.name
                )}
              </td>
              <td>{formatNumber(row.prompts)}</td>
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
