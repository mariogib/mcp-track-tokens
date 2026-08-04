import type { ReactNode } from 'react';
import {
  ChartCard,
  DailyLineChart,
  NamedBarChart,
  NamedPieChart,
} from './Charts';
import { DateRangeFilters } from './DateRangeFilters';
import { AnalysisDetailBrowse } from './AnalysisDetailBrowse';
import { MetricCard, Panel } from './MetricCard';
import { EmptyState } from './States';
import { lineValueKey, type ChartDef } from '../data/chartDefs';
import type { BreadcrumbItem } from '@lunarq/frontend-shared/components';
import { Breadcrumb, TextLink } from '../shared/adminUi';
import type { RangePreset, ResolvedRange } from '../utils/dateRange';
import { currentUtcYearMonth, toDateInputValue } from '../utils/dateRange';
import {
  computeChartStats,
  formatChartStat,
  type DaySeriesPoint,
  type NamedCostPoint,
  type NamedPromptPoint,
} from '../utils/chartDetail';
import {
  formatCurrency,
  formatDay,
  formatNumber,
  millisecondsToMinutes,
} from '../utils/format';

function averageCostPerToken(cost: number, tokens: number | undefined): number | null {
  if (tokens == null || tokens <= 0 || !Number.isFinite(cost)) {
    return null;
  }
  return cost / tokens;
}

function formatAvgCostPerToken(cost: number, tokens: number | undefined, currency: string): string {
  const avg = averageCostPerToken(cost, tokens);
  return avg == null ? '—' : formatCurrency(avg, currency, 6);
}

export type ChartDetailEntityFilter = {
  kind: 'project' | 'branch';
  label: string;
  value: string;
  options: string[];
  paramKey: 'project' | 'branch';
  emptyMessage: string;
  searchPlaceholder: string;
  nameHeader: string;
  /** When true, bar values render as plain numbers (not currency). */
  valueAsNumber?: boolean;
  renderName?: (row: NamedPromptPoint) => ReactNode;
};

export type ChartDetailAnalysisProps = {
  chartKey: string;
  def: ChartDef;
  chartTitle: string;
  range: ResolvedRange;
  currency: string;
  usingCalculatedCost: boolean;
  subtitle: ReactNode;
  breadcrumb: BreadcrumbItem[];
  backTo: string;
  backLabel: string;
  idPrefix: string;
  fromDate: string;
  toDate: string;
  rangeYear: number | null;
  rangeMonth: number | null;
  modelFilter: string;
  dayFilter: string;
  modelOptions: string[];
  dayOptions: string[];
  daySeries: DaySeriesPoint[];
  pieData: NamedCostPoint[];
  barSeries: NamedPromptPoint[];
  entityFilter?: ChartDetailEntityFilter;
  exportFilenamePrefix: string;
  onPresetChange: (next: RangePreset) => void;
  onYearMonthChange: (year: number, month: number) => void;
  updateParams: (patch: Record<string, string | null>) => void;
};

export function ChartDetailAnalysis({
  chartKey,
  def,
  chartTitle,
  range,
  currency,
  usingCalculatedCost,
  subtitle,
  breadcrumb,
  backTo,
  backLabel,
  idPrefix,
  fromDate,
  toDate,
  rangeYear,
  rangeMonth,
  modelFilter,
  dayFilter,
  modelOptions,
  dayOptions,
  daySeries,
  pieData,
  barSeries,
  entityFilter,
  exportFilenamePrefix,
  onPresetChange,
  onYearMonthChange,
  updateParams,
}: ChartDetailAnalysisProps) {
  const lineYKey = lineValueKey(chartKey);
  const stats = computeChartStats(chartKey, {
    daySeries,
    pieData,
    barSeries,
  });

  return (
    <>
      <section className="page-section">
        <div className="section-header">
          <div>
            <Breadcrumb items={breadcrumb} />
            <h2>{chartTitle}</h2>
            <p className="muted">
              {subtitle}
              {usingCalculatedCost && chartKey.includes('cost')
                ? ' · reported usage cost is $0 — using calculated token cost'
                : ''}
            </p>
          </div>
          <TextLink to={backTo} variant="muted">
            {backLabel}
          </TextLink>
        </div>

        <Panel className="stack">
          <DateRangeFilters
            idPrefix={idPrefix}
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
                <label htmlFor={`${idPrefix}-model-filter`}>Model</label>
                <select
                  id={`${idPrefix}-model-filter`}
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

          {entityFilter && def.filter === entityFilter.kind ? (
            <div className="field-row">
              <div className="field">
                <label htmlFor={`${idPrefix}-${entityFilter.kind}-filter`}>
                  {entityFilter.label}
                </label>
                <select
                  id={`${idPrefix}-${entityFilter.kind}-filter`}
                  value={entityFilter.value}
                  onChange={(e) =>
                    updateParams({ [entityFilter.paramKey]: e.target.value || null })
                  }
                >
                  <option value="">All {entityFilter.label.toLowerCase()}s</option>
                  {entityFilter.options.map((name) => (
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
                <label htmlFor={`${idPrefix}-day-filter`}>Day</label>
                <select
                  id={`${idPrefix}-day-filter`}
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
          <MetricCard label="Total" value={formatChartStat(stats.total, chartKey, currency)} />
          <MetricCard label="Average" value={formatChartStat(stats.avg, chartKey, currency)} />
          <MetricCard label="Max" value={formatChartStat(stats.max, chartKey, currency)} />
          <MetricCard label="Points" value={formatNumber(stats.count)} />
        </div>

        <ChartCard title={chartTitle} height={360}>
          {def.kind === 'line' ? (
            daySeries.length ? (
              <DailyLineChart
                data={daySeries}
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
          {def.kind === 'bar' && entityFilter ? (
            barSeries.length ? (
              <NamedBarChart
                data={barSeries}
                valueKey="prompts"
                valueLabel={def.valueLabel}
                onItemClick={(name) => updateParams({ [entityFilter.paramKey]: name })}
              />
            ) : (
              <EmptyState message={entityFilter.emptyMessage} />
            )
          ) : null}
        </ChartCard>
      </section>

      {def.kind === 'line' ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder="Search days..."
          rows={daySeries}
          getSearchText={(row) =>
            [row.day, row.dayKey, row.prompts, row.activeMinutes, row.agentMinutes, row.tokens, row.cost]
              .map(String)
              .join(' ')
          }
          exportFilename={`${exportFilenamePrefix}-${chartKey}-detail.xlsx`}
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
            day: row.day,
            prompts: row.prompts,
            activeMinutes: row.activeMinutes,
            agentMinutes: millisecondsToMinutes(row.agentDurationMilliseconds),
            tokens: row.tokens,
            cost: row.cost,
          })}
          renderTable={(rows) => <DayTable rows={rows} chartKey={chartKey} currency={currency} />}
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.dayKey} className="analysis-browse-tile">
                <strong>{row.day}</strong>
                <span>Prompts {formatNumber(row.prompts)}</span>
                <span>Active {formatNumber(row.activeMinutes)} min</span>
                <span>
                  Agent {formatNumber(millisecondsToMinutes(row.agentDurationMilliseconds))} min
                </span>
                <span>Tokens {formatNumber(row.tokens)}</span>
                <span>
                  {chartKey === 'cost-day' || row.cost > 0
                    ? formatCurrency(row.cost, currency)
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
          getSearchText={(row) =>
            [
              row.name,
              row.cost,
              row.calculatedTokenCost,
              row.tokens,
              row.rateSource,
              row.inputPerMillion,
              row.outputPerMillion,
            ]
              .filter((v) => v != null)
              .map(String)
              .join(' ')
          }
          exportFilename={`${exportFilenamePrefix}-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={
            chartKey === 'calculated-cost-by-model'
              ? [
                  { header: 'Model', key: 'name' },
                  { header: 'Rate source', key: 'rateSource' },
                  { header: 'Tokens', key: 'tokens' },
                  { header: 'Input / M', key: 'inputPerMillion' },
                  { header: 'Output / M', key: 'outputPerMillion' },
                  { header: 'Cache read / M', key: 'cacheReadPerMillion' },
                  { header: 'Cache write / M', key: 'cacheWritePerMillion' },
                  { header: 'Calculated cost', key: 'cost' },
                  { header: 'Avg cost / token', key: 'avgCostPerToken' },
                ]
              : [
                  { header: 'Model', key: 'name' },
                  { header: 'Cost', key: 'cost' },
                  { header: 'Calculated cost', key: 'calculatedTokenCost' },
                ]
          }
          toExportRow={(row) =>
            chartKey === 'calculated-cost-by-model'
              ? {
                  name: row.name,
                  rateSource: row.rateSource ?? '',
                  tokens: row.tokens ?? '',
                  inputPerMillion: row.inputPerMillion ?? '',
                  outputPerMillion: row.outputPerMillion ?? '',
                  cacheReadPerMillion: row.cacheReadPerMillion ?? '',
                  cacheWritePerMillion: row.cacheWritePerMillion ?? '',
                  cost: row.cost,
                  avgCostPerToken: averageCostPerToken(row.cost, row.tokens) ?? '',
                }
              : {
                  name: row.name,
                  cost: row.cost,
                  calculatedTokenCost: row.calculatedTokenCost ?? 0,
                }
          }
          renderTable={(rows) =>
            chartKey === 'calculated-cost-by-model' ? (
              <CalculatedModelCostTable rows={rows} currency={currency} />
            ) : (
              <NamedValueTable
                rows={rows}
                nameHeader="Model"
                valueHeader="Cost"
                currency={currency}
                showCalculatedCost
              />
            )
          }
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.name} className="analysis-browse-tile">
                <strong>{row.name}</strong>
                {chartKey === 'calculated-cost-by-model' ? (
                  <>
                    {row.rateSource ? <span className="mono">{row.rateSource}</span> : null}
                    {row.tokens != null ? <span>Tokens {formatNumber(row.tokens)}</span> : null}
                    {row.inputPerMillion != null ? (
                      <span>In/M {formatCurrency(row.inputPerMillion, currency)}</span>
                    ) : null}
                    {row.outputPerMillion != null ? (
                      <span>Out/M {formatCurrency(row.outputPerMillion, currency)}</span>
                    ) : null}
                    <span>{formatCurrency(row.cost, currency)}</span>
                    <span>
                      Avg/token {formatAvgCostPerToken(row.cost, row.tokens, currency)}
                    </span>
                  </>
                ) : (
                  <>
                    <span>{formatCurrency(row.cost, currency)}</span>
                    {(row.calculatedTokenCost ?? 0) > 0 ? (
                      <span>
                        Calculated {formatCurrency(row.calculatedTokenCost, currency)}
                      </span>
                    ) : null}
                  </>
                )}
              </article>
            ))
          }
        />
      ) : null}

      {def.kind === 'bar' && entityFilter ? (
        <AnalysisDetailBrowse
          heading="Detail data"
          searchPlaceholder={entityFilter.searchPlaceholder}
          rows={barSeries}
          getSearchText={(row) => `${row.name} ${row.prompts}`}
          exportFilename={`${exportFilenamePrefix}-${chartKey}-detail.xlsx`}
          exportTitle={chartTitle}
          exportColumns={[
            { header: entityFilter.nameHeader, key: 'name' },
            { header: 'Prompts', key: 'prompts' },
          ]}
          toExportRow={(row) => ({ name: row.name, prompts: row.prompts })}
          renderTable={(rows) =>
            entityFilter.renderName ? (
              <LinkedPromptTable
                rows={rows}
                nameHeader={entityFilter.nameHeader}
                renderName={entityFilter.renderName}
              />
            ) : (
              <NamedValueTable
                rows={rows.map((r) => ({ name: r.name, cost: r.prompts }))}
                nameHeader={entityFilter.nameHeader}
                valueHeader="Prompts"
                currency={currency}
                asNumber={entityFilter.valueAsNumber ?? true}
              />
            )
          }
          renderGrid={(rows) =>
            rows.map((row) => (
              <article key={row.projectId || row.name} className="analysis-browse-tile">
                <strong>
                  {entityFilter.renderName ? entityFilter.renderName(row) : row.name}
                </strong>
                <span>Prompts {formatNumber(row.prompts)}</span>
              </article>
            ))
          }
        />
      ) : null}
    </>
  );
}

function DayTable({
  rows,
  chartKey,
  currency,
}: {
  rows: DaySeriesPoint[];
  chartKey: string;
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
            <tr key={row.dayKey}>
              <td>{row.day}</td>
              <td>{formatNumber(row.prompts)}</td>
              <td>{formatNumber(row.activeMinutes)}</td>
              <td>{formatNumber(millisecondsToMinutes(row.agentDurationMilliseconds))}</td>
              <td>{formatNumber(row.tokens)}</td>
              <td>
                {chartKey === 'cost-day' || row.cost > 0
                  ? formatCurrency(row.cost, currency)
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
  showCalculatedCost = false,
}: {
  rows: NamedCostPoint[];
  nameHeader: string;
  valueHeader: string;
  currency: string;
  asNumber?: boolean;
  showCalculatedCost?: boolean;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>{nameHeader}</th>
          <th>{valueHeader}</th>
          {showCalculatedCost ? <th>Calculated cost</th> : null}
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
              {showCalculatedCost ? (
                <td>
                  {(row.calculatedTokenCost ?? 0) > 0
                    ? formatCurrency(row.calculatedTokenCost, currency)
                    : '—'}
                </td>
              ) : null}
            </tr>
          ))
        ) : (
          <tr>
            <td colSpan={showCalculatedCost ? 3 : 2}>No rows</td>
          </tr>
        )}
      </tbody>
    </table>
  );
}

function CalculatedModelCostTable({
  rows,
  currency,
}: {
  rows: NamedCostPoint[];
  currency: string;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>Model</th>
          <th>Rate source</th>
          <th>Tokens</th>
          <th>Input / M</th>
          <th>Output / M</th>
          <th>Cache read / M</th>
          <th>Cache write / M</th>
          <th>Calculated cost</th>
          <th>Avg cost / token</th>
        </tr>
      </thead>
      <tbody>
        {rows.length ? (
          rows.map((row) => (
            <tr key={row.name}>
              <td>{row.name}</td>
              <td className="mono">{row.rateSource || '—'}</td>
              <td>{row.tokens != null ? formatNumber(row.tokens) : '—'}</td>
              <td>
                {row.inputPerMillion != null
                  ? formatCurrency(row.inputPerMillion, currency)
                  : '—'}
              </td>
              <td>
                {row.outputPerMillion != null
                  ? formatCurrency(row.outputPerMillion, currency)
                  : '—'}
              </td>
              <td>
                {row.cacheReadPerMillion != null
                  ? formatCurrency(row.cacheReadPerMillion, currency)
                  : '—'}
              </td>
              <td>
                {row.cacheWritePerMillion != null
                  ? formatCurrency(row.cacheWritePerMillion, currency)
                  : '—'}
              </td>
              <td>{formatCurrency(row.cost, currency)}</td>
              <td>{formatAvgCostPerToken(row.cost, row.tokens, currency)}</td>
            </tr>
          ))
        ) : (
          <tr>
            <td colSpan={9}>No rows</td>
          </tr>
        )}
      </tbody>
    </table>
  );
}

function LinkedPromptTable({
  rows,
  nameHeader,
  renderName,
}: {
  rows: NamedPromptPoint[];
  nameHeader: string;
  renderName: (row: NamedPromptPoint) => ReactNode;
}) {
  return (
    <table className="data">
      <thead>
        <tr>
          <th>{nameHeader}</th>
          <th>Prompts</th>
        </tr>
      </thead>
      <tbody>
        {rows.length ? (
          rows.map((row) => (
            <tr key={row.projectId || row.name}>
              <td>{renderName(row)}</td>
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
