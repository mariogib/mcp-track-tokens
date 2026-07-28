import { useMemo, useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  useCreateProjectSessionMutation,
  useCreateTimesheetEntryMutation,
  useDeleteProjectMutation,
  useDeleteSessionMutation,
  useDeleteTimesheetEntryMutation,
  useExportMutation,
  useProjectActivityQuery,
  useProjectCostQuery,
  useProjectTokenCostQuery,
  useProjectPromptFacetsQuery,
  useProjectQuery,
  useProjectUsageQuery,
  useRecalculateActivityWindowsMutation,
  useTimesheetCategoriesQuery,
  useTimesheetReportMonthsQuery,
  useUpdateProjectMutation,
  useUpdateSessionMutation,
  useUpdateTimesheetEntryMutation,
} from '../api/hooks';
import { api } from '../api/client';
import type { PromptEventDto, SessionDto, TimesheetEntryDto } from '../api/types';
import {
  ChartCard,
  DailyLineChart,
  NamedBarChart,
  NamedPieChart,
} from '../components/Charts';
import { DateRangeFilters } from '../components/DateRangeFilters';
import { projectChartPath } from '../data/projectCharts';
import { DateTimeField, isCompleteLocalDateTime } from '../components/DateTimeField';
import { AnalysisDetailBrowse } from '../components/AnalysisDetailBrowse';
import { RemoteAnalysisDetailBrowse } from '../components/RemoteAnalysisDetailBrowse';
import { MetricCard, Panel } from '../components/MetricCard';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';
import { Breadcrumb, PopupForm, TextLink } from '../shared/adminUi';
import {
  currentUtcYearMonth,
  monthDateInputs,
  parseMonthParam,
  parseRangePreset,
  parseYearParam,
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
  millisecondsToMinutesExact,
} from '../utils/format';
import { exportProjectDetailsWorkbook } from '../utils/projectDetailsExcelExport';

const PROMPT_TIME_CUSTOM = '__custom__';

function dayBoundsUtc(dayKey: string): { fromUtc: string; toUtc: string } {
  return {
    fromUtc: `${dayKey}T00:00:00.000Z`,
    toUtc: `${dayKey}T23:59:59.999Z`,
  };
}

function resolvePromptBrowseRange(
  baseFromUtc: string,
  baseToUtc: string,
  dayFilter: string,
  customFrom: string,
  customTo: string,
): { fromUtc: string; toUtc: string } {
  if (dayFilter === PROMPT_TIME_CUSTOM) {
    return {
      fromUtc: customFrom ? `${customFrom}T00:00:00.000Z` : baseFromUtc,
      toUtc: customTo ? `${customTo}T23:59:59.999Z` : baseToUtc,
    };
  }
  if (dayFilter) {
    return dayBoundsUtc(dayFilter);
  }
  return { fromUtc: baseFromUtc, toUtc: baseToUtc };
}

const TABS = [
  'Overview',
  'Activity',
  'Prompts',
  'Sessions',
  'Timesheet',
  'Usage',
  'Cost',
  'Token Costs',
  'Repositories',
  'Exports',
  'Settings',
] as const;

const SESSION_STATUSES = ['Active', 'Paused', 'Ended', 'Abandoned'] as const;
const SESSION_EDITORS = ['Cursor', 'VisualStudioCode', 'Other'] as const;

type SessionDraft = {
  editor: string;
  status: string;
  startedAtLocal: string;
  endedAtLocal: string;
  branch: string;
  workspacePath: string;
  repositoryPath: string;
  remoteUrl: string;
  externalSessionId: string;
  editorVersion: string;
  machineName: string;
  userName: string;
};

function toLocalInputValue(iso?: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function fromLocalInputValue(local: string): string | null {
  if (!local.trim()) return null;
  const d = new Date(local);
  if (Number.isNaN(d.getTime())) return null;
  return d.toISOString();
}

function emptySessionDraft(): SessionDraft {
  return {
    editor: 'Cursor',
    status: 'Active',
    startedAtLocal: toLocalInputValue(new Date().toISOString()),
    endedAtLocal: '',
    branch: '',
    workspacePath: '',
    repositoryPath: '',
    remoteUrl: '',
    externalSessionId: '',
    editorVersion: '',
    machineName: '',
    userName: '',
  };
}

function draftFromSession(session: SessionDto): SessionDraft {
  return {
    editor: session.editor || 'Cursor',
    status: session.status || (session.isActive ? 'Active' : 'Ended'),
    startedAtLocal: toLocalInputValue(session.startedAtUtc),
    endedAtLocal: toLocalInputValue(session.endedAtUtc),
    branch: session.branch ?? '',
    workspacePath: session.workspacePath ?? '',
    repositoryPath: session.repositoryPath ?? '',
    remoteUrl: session.remoteUrl ?? '',
    externalSessionId: session.externalSessionId ?? '',
    editorVersion: session.editorVersion ?? '',
    machineName: session.machineName ?? '',
    userName: session.userName ?? '',
  };
}

function sessionDurationMs(session: SessionDto): number | null {
  const started = new Date(session.startedAtUtc).getTime();
  if (Number.isNaN(started)) return null;
  const ended = session.endedAtUtc
    ? new Date(session.endedAtUtc).getTime()
    : Date.now();
  if (Number.isNaN(ended) || ended < started) return null;
  return ended - started;
}

function timesheetDurationMs(entry: TimesheetEntryDto): number | null {
  const start = new Date(entry.startedAtUtc).getTime();
  if (Number.isNaN(start)) return null;
  const end = entry.endedAtUtc
    ? new Date(entry.endedAtUtc).getTime()
    : Date.now();
  if (Number.isNaN(end) || end < start) return null;
  return end - start;
}

type TimesheetDraft = {
  categoryId: string;
  startedAtLocal: string;
  endedAtLocal: string;
  notes: string;
};

function emptyTimesheetDraft(defaultCategoryId = ''): TimesheetDraft {
  return {
    categoryId: defaultCategoryId,
    startedAtLocal: toLocalInputValue(new Date().toISOString()),
    endedAtLocal: '',
    notes: '',
  };
}

function draftFromTimesheet(entry: TimesheetEntryDto): TimesheetDraft {
  return {
    categoryId: entry.categoryId,
    startedAtLocal: toLocalInputValue(entry.startedAtUtc),
    endedAtLocal: toLocalInputValue(entry.endedAtUtc),
    notes: entry.notes ?? '',
  };
}

export function ProjectDetailsPage() {
  const { projectId } = useParams();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [tab, setTab] = useTabSearchParam(TABS, 'Overview');
  const rangePreset = parseRangePreset(searchParams.get('range'));
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const selectedYear = parseYearParam(searchParams.get('year'));
  const selectedMonth = parseMonthParam(searchParams.get('month'));
  const range = useMemo(
    () =>
      resolveRange(
        rangePreset === 'custom' || (fromDate && toDate) ? 'custom' : rangePreset,
        fromDate,
        toDate,
        selectedYear,
        selectedMonth,
      ),
    [rangePreset, fromDate, toDate, selectedYear, selectedMonth],
  );

  const project = useProjectQuery(projectId);
  const activity = useProjectActivityQuery(projectId, range.fromUtc, range.toUtc);
  const usage = useProjectUsageQuery(projectId, range.fromUtc, range.toUtc);
  const cost = useProjectCostQuery(projectId, range.fromUtc, range.toUtc);
  const tokenCost = useProjectTokenCostQuery(projectId, range.fromUtc, range.toUtc);
  const monthsQuery = useTimesheetReportMonthsQuery(projectId);
  const promptFacets = useProjectPromptFacetsQuery(
    projectId,
    range.fromUtc,
    range.toUtc,
    tab === 'Prompts',
  );
  const [sessionBrowseEpoch, setSessionBrowseEpoch] = useState(0);
  const timesheetCategories = useTimesheetCategoriesQuery(true);
  const [timesheetBrowseEpoch, setTimesheetBrowseEpoch] = useState(0);
  const exportMutation = useExportMutation();
  const updateMutation = useUpdateProjectMutation();
  const deleteMutation = useDeleteProjectMutation();
  const createSessionMutation = useCreateProjectSessionMutation();
  const updateSessionMutation = useUpdateSessionMutation();
  const deleteSessionMutation = useDeleteSessionMutation();
  const createTimesheetMutation = useCreateTimesheetEntryMutation();
  const updateTimesheetMutation = useUpdateTimesheetEntryMutation();
  const deleteTimesheetMutation = useDeleteTimesheetEntryMutation();
  const [settingsDraft, setSettingsDraft] = useState({
    name: '',
    slug: '',
    clientName: '',
    billingCode: '',
    currency: 'USD',
    isActive: true,
  });
  const [settingsMessage, setSettingsMessage] = useState<string | null>(null);
  const [sessionEditorOpen, setSessionEditorOpen] = useState(false);
  const [editingSessionId, setEditingSessionId] = useState<string | null>(null);
  const [sessionDraft, setSessionDraft] = useState<SessionDraft>(emptySessionDraft);
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);
  const [timesheetEditorOpen, setTimesheetEditorOpen] = useState(false);
  const [editingTimesheetId, setEditingTimesheetId] = useState<string | null>(null);
  const [timesheetDraft, setTimesheetDraft] = useState<TimesheetDraft>(emptyTimesheetDraft);
  const [timesheetMessage, setTimesheetMessage] = useState<string | null>(null);
  const [promptTypeFilter, setPromptTypeFilter] = useState('');
  const [promptModelFilter, setPromptModelFilter] = useState('');
  const [promptBranchFilter, setPromptBranchFilter] = useState('');
  const [promptDayFilter, setPromptDayFilter] = useState('');
  const [promptFromDate, setPromptFromDate] = useState('');
  const [promptToDate, setPromptToDate] = useState('');
  const [overviewExporting, setOverviewExporting] = useState(false);
  const [overviewExportMessage, setOverviewExportMessage] = useState<string | null>(null);
  const [recalculateMessage, setRecalculateMessage] = useState<string | null>(null);
  const recalculateWindows = useRecalculateActivityWindowsMutation();

  const promptBrowseRange = useMemo(
    () =>
      resolvePromptBrowseRange(
        range.fromUtc,
        range.toUtc,
        promptDayFilter,
        promptFromDate,
        promptToDate,
      ),
    [promptDayFilter, promptFromDate, promptToDate, range.fromUtc, range.toUtc],
  );

  const promptFilterOptions = useMemo(() => {
    const facets = promptFacets.data;
    const sortLabels = (a: string, b: string) => a.localeCompare(b);
    return {
      types: [...(facets?.eventTypes ?? [])].sort(sortLabels),
      models: [...(facets?.models ?? [])].sort(sortLabels),
      branches: [...(facets?.branches ?? [])].sort(sortLabels),
      days: [...(facets?.days ?? [])]
        .map((dayKey) => [dayKey, formatDay(dayKey)] as const)
        .sort((a, b) => b[0].localeCompare(a[0])),
    };
  }, [promptFacets.data]);

  const onPromptTimeFilterChange = (value: string) => {
    setPromptDayFilter(value);
    if (value !== PROMPT_TIME_CUSTOM) {
      return;
    }
    if (promptFromDate || promptToDate || promptFilterOptions.days.length === 0) {
      return;
    }
    const newest = promptFilterOptions.days[0]?.[0] ?? '';
    const oldest = promptFilterOptions.days[promptFilterOptions.days.length - 1]?.[0] ?? '';
    setPromptFromDate(oldest);
    setPromptToDate(newest);
  };

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

  const onYearMonthChange = (year: number, month: number) => {
    updateParams({
      range: 'month',
      year: String(year),
      month: String(month),
      from: null,
      to: null,
    });
  };

  const onMonthSelect = (year: number, month: number) => {
    const bounds = monthDateInputs(year, month);
    updateParams({
      range: 'custom',
      from: bounds.from,
      to: bounds.to,
      year: null,
      month: null,
    });
  };

  if (project.isLoading) return <LoadingState label="Loading project…" />;
  if (project.error || !project.data) {
    return (
      <ErrorState
        message={
          project.error instanceof Error ? project.error.message : 'Project not found'
        }
      />
    );
  }

  const detail = project.data;
  const byDay = activity.data?.byDay ?? [];
  // Charts stay chronological (oldest → newest); grids use byDay as returned (newest first).
  const byDayChronological = [...byDay].sort((a, b) => a.day.localeCompare(b.day));
  const daySeries = byDayChronological.map((row) => ({
    day: formatDay(row.day),
    prompts: row.promptCount,
    activeMinutes: Math.round(row.activeProjectTimeSeconds / 60),
    agentDurationMilliseconds: row.agentDurationMilliseconds,
    agentMinutes: millisecondsToMinutesExact(row.agentDurationMilliseconds),
    tokens: row.totalTokens ?? 0,
  }));

  // When Cursor exports are Included/Free, reported totalAiCost is $0 — use rate-card
  // calculatedTokenCost so overview charts stay meaningful.
  const reportedTotalCost = cost.data?.totalAiCost ?? 0;
  const calculatedTotalCost = cost.data?.calculatedTokenCost ?? 0;
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const usingCalculatedCost = reportedTotalCost <= 0 && calculatedTotalCost > 0;

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

  const modelCostSeries = (cost.data?.byModel ?? [])
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.usageBasedCost + m.subscriptionAllocation,
    }))
    .filter((m) => m.cost > 0);

  const modelCalculatedSeries = (cost.data?.byModel ?? [])
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.calculatedTokenCost ?? 0,
    }))
    .filter((m) => m.cost > 0);

  const branchSeries = (activity.data?.byBranch ?? []).map((b) => ({
    name: b.name || '(none)',
    prompts: b.promptCount,
  }));

  const onExportProjectWorkbook = () => {
    if (overviewExporting) return;
    setOverviewExportMessage(null);
    setOverviewExporting(true);
    void exportProjectDetailsWorkbook({
      project: detail,
      fromUtc: range.fromUtc,
      toUtc: range.toUtc,
      activity: activity.data,
      usage: usage.data,
      cost: cost.data,
      tokenCost: tokenCost.data,
    })
      .then(() => {
        setOverviewExportMessage(null);
      })
      .catch((err: unknown) => {
        setOverviewExportMessage(err instanceof Error ? err.message : 'Export failed');
      })
      .finally(() => {
        setOverviewExporting(false);
      });
  };

  const onRecalculateTime = () => {
    if (recalculateWindows.isPending) return;
    setRecalculateMessage(null);
    recalculateWindows.mutate(
      {
        projectId: detail.id,
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        dryRun: false,
      },
      {
        onSuccess: (result) => {
          const hours = Math.floor(result.totalActiveSeconds / 3600);
          const minutes = Math.floor((result.totalActiveSeconds % 3600) / 60);
          setRecalculateMessage(
            `Recalculated ${formatNumber(result.windowCount)} activity window${result.windowCount === 1 ? '' : 's'} · ${hours}h ${minutes}m active time.`,
          );
        },
        onError: (err: unknown) => {
          setRecalculateMessage(
            err instanceof Error ? err.message : 'Failed to re-calculate time.',
          );
        },
      },
    );
  };

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <Breadcrumb
              items={[
                { label: 'Projects', to: '/projects' },
                { label: detail.name },
              ]}
            />
            <h2>{detail.name}</h2>
            <p>
              {detail.clientName ?? 'No client'} · {detail.slug}
            </p>
          </div>
          <StatusBadge
            label={detail.isActive ? 'Active' : 'Inactive'}
            tone={detail.isActive ? 'success' : 'neutral'}
          />
        </div>

        <div className="tabs" role="tablist" aria-label="Project sections">
          {TABS.map((name) => (
            <button
              key={name}
              type="button"
              role="tab"
              aria-selected={tab === name}
              className={`tab${tab === name ? ' active' : ''}`}
              onClick={() => {
                setTab(name);
                if (name === 'Settings') {
                  setSettingsDraft({
                    name: detail.name,
                    slug: detail.slug,
                    clientName: detail.clientName ?? '',
                    billingCode: detail.billingCode ?? '',
                    currency: detail.currency || 'USD',
                    isActive: detail.isActive,
                  });
                }
              }}
            >
              {name}
            </button>
          ))}
        </div>

        <Panel>
          <div className="field-row">
            <div className="field">
              <label className="label">Period</label>
              <p className="hint">{range.label}</p>
            </div>
          </div>
          <DateRangeFilters
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
                from: fromDate || toDateInputValue(range.fromUtc),
                to: value,
                year: null,
                month: null,
              })
            }
            year={selectedYear ?? currentUtcYearMonth().year}
            month={selectedMonth ?? currentUtcYearMonth().month}
            onYearMonthChange={onYearMonthChange}
            monthsWithData={monthsQuery.data}
            onMonthSelect={onMonthSelect}
            idPrefix="project-details-range"
          />
        </Panel>
      </section>

      {tab === 'Overview' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Overview</h2>
              <p className="muted">
                Activity, usage, and cost for {range.label}. Re-calculate Time rebuilds prompt and
                session activity windows for this range. Export builds an Excel workbook with a
                sheet for each data tab.
              </p>
            </div>
            <div className="section-actions">
              <button
                type="button"
                className="btn btn-secondary"
                disabled={recalculateWindows.isPending || overviewExporting}
                onClick={onRecalculateTime}
              >
                {recalculateWindows.isPending ? 'Re-calculating…' : 'Re-calculate Time'}
              </button>
              <button
                type="button"
                className="btn"
                disabled={overviewExporting || recalculateWindows.isPending}
                onClick={onExportProjectWorkbook}
              >
                {overviewExporting ? 'Exporting…' : 'Export to Excel'}
              </button>
            </div>
          </div>
          {recalculateMessage ? (
            <p className="form-message" role="status">
              {recalculateMessage}
            </p>
          ) : null}
          {overviewExportMessage ? (
            <p className="form-message" role="alert">
              {overviewExportMessage}
            </p>
          ) : null}
          <div className="metric-grid">
            <MetricCard label="Prompts" value={formatNumber(activity.data?.promptCount ?? detail.activity?.promptCount)} />
            <MetricCard
              label="Agent time"
              value={formatDurationMs(
                activity.data?.agentDurationMilliseconds ?? detail.activity?.agentDurationMilliseconds,
              )}
            />
            <MetricCard
              label="Active time"
              value={formatDurationSeconds(
                activity.data?.activeProjectTimeSeconds ?? detail.activity?.activeProjectTimeSeconds,
              )}
            />
            <MetricCard
              label="Total tokens"
              value={formatNumber(
                cost.data?.importedTotalTokens ?? usage.data?.totalTokens ?? 0,
              )}
            />
            <MetricCard
              label={usingCalculatedCost ? 'Calculated token cost' : 'Total AI cost'}
              value={formatCurrency(
                displayTotalCost || (detail.cost?.totalAiCost ?? 0),
                cost.data?.currency ?? detail.currency,
              )}
              hint={
                usingCalculatedCost
                  ? 'Reported usage cost is $0 — showing Settings rate card × tokens'
                  : undefined
              }
            />
          </div>
          <div className="chart-grid">
            <ChartCard
              title="Prompts / day"
              to={projectId ? projectChartPath(projectId, 'prompts-day') : undefined}
            >
              <DailyLineChart data={daySeries} xKey="day" yKey="prompts" yLabel="Prompts" />
            </ChartCard>
            <ChartCard
              title="Active time / day (minutes)"
              to={projectId ? projectChartPath(projectId, 'active-time-day') : undefined}
            >
              <DailyLineChart data={daySeries} xKey="day" yKey="activeMinutes" yLabel="Minutes" />
            </ChartCard>
            <ChartCard
              title="Agent duration / day (minutes)"
              to={projectId ? projectChartPath(projectId, 'agent-duration-day') : undefined}
            >
              <DailyLineChart data={daySeries} xKey="day" yKey="agentMinutes" yLabel="Minutes" />
            </ChartCard>
            <ChartCard
              title={usingCalculatedCost ? 'Calculated cost / day' : 'Cost / day'}
              to={projectId ? projectChartPath(projectId, 'cost-day') : undefined}
            >
              <DailyLineChart data={costByDay} xKey="day" yKey="cost" yLabel="Cost" />
            </ChartCard>
            <ChartCard
              title="Tokens / day"
              to={projectId ? projectChartPath(projectId, 'tokens-day') : undefined}
            >
              <DailyLineChart data={daySeries} xKey="day" yKey="tokens" yLabel="Tokens" />
            </ChartCard>
            <ChartCard
              title="Cost by model"
              to={projectId ? projectChartPath(projectId, 'cost-by-model') : undefined}
            >
              {modelCostSeries.length ? (
                <NamedPieChart data={modelCostSeries} valueKey="cost" />
              ) : (
                <EmptyState message="No reported model cost in range (usage/subscription)." />
              )}
            </ChartCard>
            <ChartCard
              title="Calculated cost by model"
              to={projectId ? projectChartPath(projectId, 'calculated-cost-by-model') : undefined}
            >
              {modelCalculatedSeries.length ? (
                <NamedPieChart data={modelCalculatedSeries} valueKey="cost" />
              ) : (
                <EmptyState message="No calculated token cost in range." />
              )}
            </ChartCard>
            <ChartCard
              title="Activity by branch"
              to={projectId ? projectChartPath(projectId, 'activity-by-branch') : undefined}
            >
              {branchSeries.length ? (
                <NamedBarChart data={branchSeries} valueKey="prompts" valueLabel="Prompts" />
              ) : (
                <EmptyState message="No branch activity in range." />
              )}
            </ChartCard>
          </div>
        </section>
      )}

      {tab === 'Activity' &&
        (activity.isLoading ? (
          <LoadingState />
        ) : activity.error ? (
          <ErrorState
            message={activity.error instanceof Error ? activity.error.message : 'Failed'}
          />
        ) : (
          <AnalysisDetailBrowse
            heading="Activity by day"
            searchPlaceholder="Search days..."
            rows={activity.data?.byDay ?? []}
            getSearchText={(row) =>
              [
                formatDay(row.day),
                row.promptCount,
                row.agentRuns,
                row.sessionCount,
              ]
                .map(String)
                .join(' ')
            }
            exportFilename={`project-${detail.id}-activity.xlsx`}
            exportTitle={`${detail.name} · Activity`}
            exportColumns={[
              { header: 'Day', key: 'day' },
              { header: 'Prompts', key: 'promptCount' },
              { header: 'Agent runs', key: 'agentRuns' },
              { header: 'Agent duration (ms)', key: 'agentDurationMilliseconds' },
              { header: 'Active time (s)', key: 'activeProjectTimeSeconds' },
              { header: 'Sessions', key: 'sessionCount' },
            ]}
            toExportRow={(row) => ({
              day: formatDay(row.day),
              promptCount: row.promptCount,
              agentRuns: row.agentRuns,
              agentDurationMilliseconds: row.agentDurationMilliseconds,
              activeProjectTimeSeconds: row.activeProjectTimeSeconds,
              sessionCount: row.sessionCount,
            })}
            emptySourceMessage="No activity in the selected range."
            renderTable={(rows) => (
              <table className="data">
                <thead>
                  <tr>
                    <th>Day</th>
                    <th>Prompts</th>
                    <th>Agent runs</th>
                    <th>Agent duration</th>
                    <th>Active time</th>
                    <th>Sessions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.day}>
                      <td>{formatDay(row.day)}</td>
                      <td>{formatNumber(row.promptCount)}</td>
                      <td>{formatNumber(row.agentRuns)}</td>
                      <td>{formatDurationMs(row.agentDurationMilliseconds)}</td>
                      <td>{formatDurationSeconds(row.activeProjectTimeSeconds)}</td>
                      <td>{formatNumber(row.sessionCount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            renderGrid={(rows) =>
              rows.map((row) => (
                <article key={row.day} className="analysis-browse-tile">
                  <strong>{formatDay(row.day)}</strong>
                  <span>Prompts {formatNumber(row.promptCount)}</span>
                  <span>Agent runs {formatNumber(row.agentRuns)}</span>
                  <span>
                    Active {formatDurationSeconds(row.activeProjectTimeSeconds)}
                  </span>
                  <span>Sessions {formatNumber(row.sessionCount)}</span>
                </article>
              ))
            }
          />
        ))}

      {tab === 'Prompts' && (
          <RemoteAnalysisDetailBrowse<PromptEventDto>
            heading="Prompts"
            searchPlaceholder="Search prompts..."
            filterKey={[
              projectId,
              promptBrowseRange.fromUtc,
              promptBrowseRange.toUtc,
              promptTypeFilter,
              promptModelFilter,
              promptBranchFilter,
              promptDayFilter,
              promptFromDate,
              promptToDate,
            ].join('|')}
            fetchPage={async ({ pageIndex, pageSize, search, status, signal }) =>
              api.getProjectPromptsPaged(
                projectId!,
                {
                  fromUtc: promptBrowseRange.fromUtc,
                  toUtc: promptBrowseRange.toUtc,
                  pageIndex,
                  pageSize,
                  search: search || undefined,
                  status: status || undefined,
                  eventType: promptTypeFilter || undefined,
                  model: promptModelFilter || undefined,
                  branch: promptBranchFilter || undefined,
                },
                signal,
              )
            }
            getStatusValue={(p) => p.status?.trim() || 'None'}
            filters={[
              {
                id: 'prompt-type-filter',
                label: 'Type',
                value: promptTypeFilter,
                onChange: setPromptTypeFilter,
                options: [
                  { value: '', label: 'All types' },
                  ...promptFilterOptions.types.map((value) => ({ value, label: value })),
                ],
              },
              {
                id: 'prompt-model-filter',
                label: 'Model',
                value: promptModelFilter,
                onChange: setPromptModelFilter,
                options: [
                  { value: '', label: 'All models' },
                  ...promptFilterOptions.models.map((value) => ({ value, label: value })),
                ],
              },
              {
                id: 'prompt-branch-filter',
                label: 'Branch',
                value: promptBranchFilter,
                onChange: setPromptBranchFilter,
                options: [
                  { value: '', label: 'All branches' },
                  ...promptFilterOptions.branches.map((value) => ({ value, label: value })),
                ],
              },
              {
                id: 'prompt-day-filter',
                label: 'Time',
                value: promptDayFilter,
                onChange: onPromptTimeFilterChange,
                options: [
                  { value: '', label: 'All days' },
                  { value: PROMPT_TIME_CUSTOM, label: 'Custom range' },
                  ...promptFilterOptions.days.map(([value, label]) => ({ value, label })),
                ],
              },
            ]}
            filtersExtra={
              promptDayFilter === PROMPT_TIME_CUSTOM ? (
                <div className="field-row chart-detail-filters">
                  <div className="field">
                    <label htmlFor="prompt-time-from">From</label>
                    <input
                      id="prompt-time-from"
                      type="date"
                      value={promptFromDate}
                      max={promptToDate || undefined}
                      onChange={(e) => setPromptFromDate(e.target.value)}
                    />
                  </div>
                  <div className="field">
                    <label htmlFor="prompt-time-to">To</label>
                    <input
                      id="prompt-time-to"
                      type="date"
                      value={promptToDate}
                      min={promptFromDate || undefined}
                      onChange={(e) => setPromptToDate(e.target.value)}
                    />
                  </div>
                  {(promptFromDate || promptToDate) && (
                    <p className="hint" style={{ alignSelf: 'end' }}>
                      Custom range
                      {promptFromDate ? ` from ${promptFromDate}` : ''}
                      {promptToDate ? ` to ${promptToDate}` : ''}
                    </p>
                  )}
                </div>
              ) : null
            }
            exportFilename={`project-${detail.id}-prompts.xlsx`}
            exportTitle={`${detail.name} · Prompts`}
            exportColumns={[
              { header: 'Time', key: 'timestampUtc' },
              { header: 'Type', key: 'eventType' },
              { header: 'Editor', key: 'editor' },
              { header: 'Model', key: 'model' },
              { header: 'Branch', key: 'branch' },
              { header: 'Status', key: 'status' },
              { header: 'Duration (ms)', key: 'durationMilliseconds' },
              { header: 'Linked usages', key: 'linkedUsageCount' },
              { header: 'Total tokens', key: 'totalTokens' },
              { header: 'Cost', key: 'reportedCost' },
              { header: 'Calculated cost', key: 'calculatedTokenCost' },
            ]}
            toExportRow={(p) => ({
              timestampUtc: formatDateTime(p.timestampUtc),
              eventType: p.eventType,
              editor: p.editor ?? '',
              model: p.model ?? '',
              branch: p.branch ?? '',
              status: p.status ?? '',
              durationMilliseconds: p.durationMilliseconds ?? '',
              linkedUsageCount: p.linkedUsageCount ?? '',
              totalTokens: p.totalTokens ?? '',
              reportedCost: p.reportedCost ?? '',
              calculatedTokenCost: p.calculatedTokenCost ?? '',
            })}
            emptySourceMessage="No prompts in the selected range."
            emptyMessage="No prompts match the current search or filters."
            renderTable={(rows) => (
              <table className="data">
                <thead>
                  <tr>
                    <th>Time</th>
                    <th>Type</th>
                    <th>Editor</th>
                    <th>Model</th>
                    <th>Branch</th>
                    <th>Status</th>
                    <th>Duration</th>
                    <th>Linked usages</th>
                    <th>Total Tokens</th>
                    <th>Cost</th>
                    <th>Calculated cost</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((p) => (
                    <tr key={p.id}>
                      <td>{formatDateTime(p.timestampUtc)}</td>
                      <td>{p.eventType}</td>
                      <td>{p.editor ?? '—'}</td>
                      <td>{p.model ?? '—'}</td>
                      <td>{p.branch ?? '—'}</td>
                      <td>{p.status ?? '—'}</td>
                      <td>{formatDurationMs(p.durationMilliseconds)}</td>
                      <td>
                        {p.hasLinkedUsage || (p.linkedUsageCount ?? 0) > 0
                          ? formatNumber(p.linkedUsageCount ?? 0)
                          : '—'}
                      </td>
                      <td>
                        {p.hasLinkedUsage || p.totalTokens != null
                          ? formatNumber(p.totalTokens ?? 0)
                          : '—'}
                      </td>
                      <td>
                        {p.hasLinkedUsage || p.reportedCost != null
                          ? formatCurrency(p.reportedCost ?? 0)
                          : '—'}
                      </td>
                      <td>
                        {p.hasLinkedUsage || p.calculatedTokenCost != null
                          ? formatCurrency(p.calculatedTokenCost ?? 0)
                          : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            renderGrid={(rows) =>
              rows.map((p) => (
                <article key={p.id} className="analysis-browse-tile">
                  <strong>{formatDateTime(p.timestampUtc)}</strong>
                  <span>
                    {p.eventType}
                    {p.model ? ` · ${p.model}` : ''}
                  </span>
                  <span>{p.branch ?? 'No branch'}</span>
                  <span>
                    {p.hasLinkedUsage || p.calculatedTokenCost != null
                      ? formatCurrency(p.calculatedTokenCost ?? 0)
                      : '—'}
                  </span>
                </article>
              ))
            }
          />
      )}

      {tab === 'Sessions' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Sessions</h2>
              <p className="muted">Add, edit, or delete tracked editor sessions for this project.</p>
            </div>
            <button
              type="button"
              className="btn"
              onClick={() => {
                setEditingSessionId(null);
                setSessionDraft(emptySessionDraft());
                setSessionMessage(null);
                setSessionEditorOpen(true);
              }}
            >
              Add session
            </button>
          </div>

          {sessionEditorOpen ? (
            <PopupForm
              title={editingSessionId ? 'Edit session' : 'New session'}
              onClose={() => {
                setSessionEditorOpen(false);
                setEditingSessionId(null);
                setSessionMessage(null);
              }}
              onSubmit={(e) => {
                const event = e as FormEvent;
                event.preventDefault();
                void (async () => {
                  setSessionMessage(null);
                  if (!isCompleteLocalDateTime(sessionDraft.startedAtLocal)) {
                    setSessionMessage('Started date and time are required.');
                    return;
                  }
                  const startedAtUtc = fromLocalInputValue(sessionDraft.startedAtLocal);
                  if (!startedAtUtc) {
                    setSessionMessage('Started date and time are invalid.');
                    return;
                  }
                  if (
                    sessionDraft.endedAtLocal.trim() &&
                    !isCompleteLocalDateTime(sessionDraft.endedAtLocal)
                  ) {
                    setSessionMessage('Ended date and time are incomplete.');
                    return;
                  }
                  const endedAtUtc = fromLocalInputValue(sessionDraft.endedAtLocal);
                  if (
                    endedAtUtc &&
                    new Date(endedAtUtc).getTime() < new Date(startedAtUtc).getTime()
                  ) {
                    setSessionMessage('Ended time cannot be earlier than started time.');
                    return;
                  }
                  const payload = {
                    editor: sessionDraft.editor,
                    status: sessionDraft.status,
                    startedAtUtc,
                    endedAtUtc,
                    branch: sessionDraft.branch.trim() || null,
                    workspacePath: sessionDraft.workspacePath.trim() || null,
                    repositoryPath: sessionDraft.repositoryPath.trim() || null,
                    remoteUrl: sessionDraft.remoteUrl.trim() || null,
                    externalSessionId: sessionDraft.externalSessionId.trim() || null,
                    editorVersion: sessionDraft.editorVersion.trim() || null,
                    machineName: sessionDraft.machineName.trim() || null,
                    userName: sessionDraft.userName.trim() || null,
                  };
                  try {
                    if (editingSessionId) {
                      await updateSessionMutation.mutateAsync({
                        id: editingSessionId,
                        body: {
                          ...payload,
                          projectId: detail.id,
                          status: sessionDraft.status,
                          startedAtUtc,
                        },
                      });
                      setSessionMessage('Session updated.');
                    } else {
                      await createSessionMutation.mutateAsync({
                        projectId: detail.id,
                        body: payload,
                      });
                      setSessionMessage('Session created.');
                    }
                    setSessionEditorOpen(false);
                    setEditingSessionId(null);
                    setSessionBrowseEpoch((value) => value + 1);
                  } catch (err) {
                    setSessionMessage(err instanceof Error ? err.message : 'Save failed');
                  }
                })();
              }}
              footer={
                <>
                  <button
                    type="submit"
                    className="btn"
                    disabled={createSessionMutation.isPending || updateSessionMutation.isPending}
                  >
                    {createSessionMutation.isPending || updateSessionMutation.isPending
                      ? 'Saving…'
                      : editingSessionId
                        ? 'Save session'
                        : 'Create session'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => {
                      setSessionEditorOpen(false);
                      setEditingSessionId(null);
                      setSessionMessage(null);
                    }}
                  >
                    Cancel
                  </button>
                </>
              }
            >
              <div className="stack">
                <div className="field-row">
                  <div className="field">
                    <label htmlFor="session-editor">Editor</label>
                    <select
                      id="session-editor"
                      value={sessionDraft.editor}
                      onChange={(e) => setSessionDraft((s) => ({ ...s, editor: e.target.value }))}
                    >
                      {SESSION_EDITORS.map((editor) => (
                        <option key={editor} value={editor}>
                          {editor}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="field">
                    <label htmlFor="session-status">Status</label>
                    <select
                      id="session-status"
                      value={sessionDraft.status}
                      onChange={(e) => setSessionDraft((s) => ({ ...s, status: e.target.value }))}
                    >
                      {SESSION_STATUSES.map((status) => (
                        <option key={status} value={status}>
                          {status}
                        </option>
                      ))}
                    </select>
                  </div>
                  <DateTimeField
                    id="session-started"
                    label="Started"
                    required
                    value={sessionDraft.startedAtLocal}
                    onChange={(startedAtLocal) =>
                      setSessionDraft((s) => ({ ...s, startedAtLocal }))
                    }
                  />
                  <DateTimeField
                    id="session-ended"
                    label="Ended"
                    value={sessionDraft.endedAtLocal}
                    onChange={(endedAtLocal) => setSessionDraft((s) => ({ ...s, endedAtLocal }))}
                  />
                </div>
                <div className="field-row">
                  <div className="field">
                    <label htmlFor="session-branch">Branch</label>
                    <input
                      id="session-branch"
                      value={sessionDraft.branch}
                      onChange={(e) => setSessionDraft((s) => ({ ...s, branch: e.target.value }))}
                    />
                  </div>
                  <div className="field">
                    <label htmlFor="session-workspace">Workspace path</label>
                    <input
                      id="session-workspace"
                      value={sessionDraft.workspacePath}
                      onChange={(e) =>
                        setSessionDraft((s) => ({ ...s, workspacePath: e.target.value }))
                      }
                    />
                  </div>
                  <div className="field">
                    <label htmlFor="session-repo">Repository path</label>
                    <input
                      id="session-repo"
                      value={sessionDraft.repositoryPath}
                      onChange={(e) =>
                        setSessionDraft((s) => ({ ...s, repositoryPath: e.target.value }))
                      }
                    />
                  </div>
                </div>
                <div className="field-row">
                  <div className="field">
                    <label htmlFor="session-remote">Remote URL</label>
                    <input
                      id="session-remote"
                      value={sessionDraft.remoteUrl}
                      onChange={(e) => setSessionDraft((s) => ({ ...s, remoteUrl: e.target.value }))}
                    />
                  </div>
                  <div className="field">
                    <label htmlFor="session-external">External session id</label>
                    <input
                      id="session-external"
                      value={sessionDraft.externalSessionId}
                      onChange={(e) =>
                        setSessionDraft((s) => ({ ...s, externalSessionId: e.target.value }))
                      }
                    />
                  </div>
                </div>
              </div>
            </PopupForm>
          ) : null}

          {sessionMessage ? (
            <p className="form-message">{sessionMessage}</p>
          ) : null}

          <RemoteAnalysisDetailBrowse<SessionDto>
            embedded
            heading="Sessions"
            showHeading={false}
            searchPlaceholder="Search sessions..."
            filterKey={[projectId, range.fromUtc, range.toUtc, sessionBrowseEpoch].join('|')}
            fetchPage={async ({ pageIndex, pageSize, search, status, signal }) =>
              api.getProjectSessionsPaged(
                projectId!,
                {
                  fromUtc: range.fromUtc,
                  toUtc: range.toUtc,
                  pageIndex,
                  pageSize,
                  search: search || undefined,
                  status: status || undefined,
                },
                signal,
              )
            }
            getStatusValue={(s) => s.status || (s.isActive ? 'Active' : 'Closed')}
            statusOptions={[
              ...SESSION_STATUSES.map((status) => ({ value: status, label: status })),
              { value: 'Closed', label: 'Closed' },
            ]}
            exportFilename={`project-${detail.id}-sessions.xlsx`}
            exportTitle={`${detail.name} · Sessions`}
            exportColumns={[
              { header: 'Session', key: 'id' },
              { header: 'Editor', key: 'editor' },
              { header: 'Started', key: 'startedAtUtc' },
              { header: 'Ended', key: 'endedAtUtc' },
              { header: 'Duration (ms)', key: 'durationMilliseconds' },
              { header: 'Branch', key: 'branch' },
              { header: 'Status', key: 'status' },
            ]}
            toExportRow={(s) => ({
              id: s.id,
              editor: s.editor ?? '',
              startedAtUtc: formatDateTime(s.startedAtUtc),
              endedAtUtc: formatDateTime(s.endedAtUtc),
              durationMilliseconds: sessionDurationMs(s) ?? '',
              branch: s.branch ?? '',
              status: s.status || (s.isActive ? 'Active' : 'Closed'),
            })}
            emptySourceMessage="No sessions in the selected range."
            renderTable={(rows) => (
              <table className="data">
                <thead>
                  <tr>
                    <th>Session</th>
                    <th>Editor</th>
                    <th>Started</th>
                    <th>Ended</th>
                    <th>Duration</th>
                    <th>Branch</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((s) => {
                    const durationMs = sessionDurationMs(s);
                    return (
                      <tr key={s.id}>
                        <td className="mono">{s.id.slice(0, 8)}</td>
                        <td>{s.editor ?? '—'}</td>
                        <td>{formatDateTime(s.startedAtUtc)}</td>
                        <td>{formatDateTime(s.endedAtUtc)}</td>
                        <td>{durationMs == null ? '—' : formatDurationMs(durationMs)}</td>
                        <td>{s.branch ?? '—'}</td>
                        <td>
                          <StatusBadge
                            label={s.status || (s.isActive ? 'Active' : 'Closed')}
                            tone={
                              s.isActive || s.status === 'Active' ? 'success' : 'neutral'
                            }
                          />
                        </td>
                        <td>
                          <div className="row-actions">
                            <button
                              type="button"
                              className="btn btn-compact btn-secondary"
                              onClick={() => {
                                setEditingSessionId(s.id);
                                setSessionDraft(draftFromSession(s));
                                setSessionMessage(null);
                                setSessionEditorOpen(true);
                              }}
                            >
                              Edit
                            </button>
                            <button
                              type="button"
                              className="btn btn-compact btn-danger"
                              disabled={deleteSessionMutation.isPending}
                              onClick={() => {
                                const ok = window.confirm(
                                  `Delete session ${s.id.slice(0, 8)}…? Linked activity stays, but loses this session link.`,
                                );
                                if (!ok) return;
                                void deleteSessionMutation
                                  .mutateAsync({ id: s.id, projectId: detail.id })
                                  .then(() => {
                                    setSessionMessage(null);
                                    setSessionBrowseEpoch((value) => value + 1);
                                  })
                                  .catch((err: unknown) => {
                                    setSessionMessage(
                                      err instanceof Error ? err.message : 'Delete failed',
                                    );
                                  });
                              }}
                            >
                              Delete
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
            renderGrid={(rows) =>
              rows.map((s) => {
                const durationMs = sessionDurationMs(s);
                return (
                  <article key={s.id} className="analysis-browse-tile">
                    <strong className="mono">{s.id.slice(0, 8)}</strong>
                    <span>{s.editor ?? '—'}</span>
                    <span>{formatDateTime(s.startedAtUtc)}</span>
                    <span>
                      {durationMs == null ? '—' : formatDurationMs(durationMs)}
                    </span>
                    <span>{s.branch ?? 'No branch'}</span>
                    <div className="row-actions">
                      <button
                        type="button"
                        className="btn btn-compact btn-secondary"
                        onClick={() => {
                          setEditingSessionId(s.id);
                          setSessionDraft(draftFromSession(s));
                          setSessionMessage(null);
                          setSessionEditorOpen(true);
                        }}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="btn btn-compact btn-danger"
                        disabled={deleteSessionMutation.isPending}
                        onClick={() => {
                          const ok = window.confirm(
                            `Delete session ${s.id.slice(0, 8)}…? Linked activity stays, but loses this session link.`,
                          );
                          if (!ok) return;
                          void deleteSessionMutation
                            .mutateAsync({ id: s.id, projectId: detail.id })
                            .then(() => {
                              setSessionMessage(null);
                              setSessionBrowseEpoch((value) => value + 1);
                            })
                            .catch((err: unknown) => {
                              setSessionMessage(
                                err instanceof Error ? err.message : 'Delete failed',
                              );
                            });
                        }}
                      >
                        Delete
                      </button>
                    </div>
                  </article>
                );
              })
            }
          />
        </section>
      )}

      {tab === 'Timesheet' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Timesheet</h2>
              <p className="muted">
                Capture billable time with category, start, end, and notes. MCP tools{' '}
                <code>start_timesheet</code> / <code>end_timesheet</code> write here for the open
                Cursor project. Categories are managed under Settings → Data.
              </p>
            </div>
            <button
              type="button"
              className="btn"
              onClick={() => {
                const defaultCategoryId =
                  timesheetCategories.data?.find((c) =>
                    c.name.toLowerCase() === 'work',
                  )?.id ??
                  timesheetCategories.data?.[0]?.id ??
                  '';
                setEditingTimesheetId(null);
                setTimesheetDraft(emptyTimesheetDraft(defaultCategoryId));
                setTimesheetMessage(null);
                setTimesheetEditorOpen(true);
              }}
            >
              Add entry
            </button>
          </div>

          {timesheetEditorOpen ? (
            <PopupForm
              title={editingTimesheetId ? 'Edit timesheet entry' : 'New timesheet entry'}
              onClose={() => {
                setTimesheetEditorOpen(false);
                setEditingTimesheetId(null);
                setTimesheetMessage(null);
              }}
              onSubmit={(e) => {
                const event = e as FormEvent;
                event.preventDefault();
                void (async () => {
                  setTimesheetMessage(null);
                  if (!isCompleteLocalDateTime(timesheetDraft.startedAtLocal)) {
                    setTimesheetMessage('Started date and time are required.');
                    return;
                  }
                  const startedAtUtc = fromLocalInputValue(timesheetDraft.startedAtLocal);
                  if (!startedAtUtc) {
                    setTimesheetMessage('Started date and time are invalid.');
                    return;
                  }
                  if (
                    timesheetDraft.endedAtLocal.trim() &&
                    !isCompleteLocalDateTime(timesheetDraft.endedAtLocal)
                  ) {
                    setTimesheetMessage('Ended date and time are incomplete.');
                    return;
                  }
                  const endedAtUtc = fromLocalInputValue(timesheetDraft.endedAtLocal);
                  if (
                    endedAtUtc &&
                    new Date(endedAtUtc).getTime() < new Date(startedAtUtc).getTime()
                  ) {
                    setTimesheetMessage('Ended time cannot be earlier than started time.');
                    return;
                  }
                  if (!timesheetDraft.categoryId) {
                    setTimesheetMessage('Category is required.');
                    return;
                  }
                  const payload = {
                    categoryId: timesheetDraft.categoryId,
                    startedAtUtc,
                    endedAtUtc,
                    notes: timesheetDraft.notes.trim() || null,
                  };
                  try {
                    if (editingTimesheetId) {
                      await updateTimesheetMutation.mutateAsync({
                        id: editingTimesheetId,
                        body: {
                          categoryId: payload.categoryId,
                          startedAtUtc,
                          endedAtUtc,
                          notes: payload.notes,
                        },
                      });
                      setTimesheetMessage('Timesheet entry updated.');
                    } else {
                      await createTimesheetMutation.mutateAsync({
                        projectId: detail.id,
                        body: payload,
                      });
                      setTimesheetMessage('Timesheet entry created.');
                    }
                    setTimesheetEditorOpen(false);
                    setEditingTimesheetId(null);
                    await Promise.resolve();
                    setTimesheetBrowseEpoch((value) => value + 1);
                  } catch (err) {
                    setTimesheetMessage(err instanceof Error ? err.message : 'Save failed');
                  }
                })();
              }}
              footer={
                <>
                  <button
                    type="submit"
                    className="btn"
                    disabled={
                      createTimesheetMutation.isPending || updateTimesheetMutation.isPending
                    }
                  >
                    {createTimesheetMutation.isPending || updateTimesheetMutation.isPending
                      ? 'Saving…'
                      : editingTimesheetId
                        ? 'Save entry'
                        : 'Create entry'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => {
                      setTimesheetEditorOpen(false);
                      setEditingTimesheetId(null);
                      setTimesheetMessage(null);
                    }}
                  >
                    Cancel
                  </button>
                </>
              }
            >
              <div className="stack">
                <div className="field">
                  <label htmlFor="timesheet-category">Category</label>
                  <select
                    id="timesheet-category"
                    required
                    value={timesheetDraft.categoryId}
                    onChange={(e) =>
                      setTimesheetDraft((s) => ({ ...s, categoryId: e.target.value }))
                    }
                  >
                    <option value="" disabled>
                      Select category…
                    </option>
                    {(timesheetCategories.data ?? []).map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                    {editingTimesheetId &&
                    timesheetDraft.categoryId &&
                    !(timesheetCategories.data ?? []).some(
                      (c) => c.id === timesheetDraft.categoryId,
                    ) ? (
                      <option value={timesheetDraft.categoryId}>Inactive category</option>
                    ) : null}
                  </select>
                </div>
                <div className="field-row">
                  <DateTimeField
                    id="timesheet-started"
                    label="Started"
                    required
                    value={timesheetDraft.startedAtLocal}
                    onChange={(startedAtLocal) =>
                      setTimesheetDraft((s) => ({ ...s, startedAtLocal }))
                    }
                  />
                  <DateTimeField
                    id="timesheet-ended"
                    label="Ended"
                    value={timesheetDraft.endedAtLocal}
                    onChange={(endedAtLocal) =>
                      setTimesheetDraft((s) => ({ ...s, endedAtLocal }))
                    }
                  />
                </div>
                <div className="field">
                  <label htmlFor="timesheet-notes">Notes</label>
                  <textarea
                    id="timesheet-notes"
                    value={timesheetDraft.notes}
                    onChange={(e) => setTimesheetDraft((s) => ({ ...s, notes: e.target.value }))}
                    rows={4}
                  />
                </div>
              </div>
            </PopupForm>
          ) : null}

          {timesheetMessage ? (
            <p className="form-message">{timesheetMessage}</p>
          ) : null}

          <RemoteAnalysisDetailBrowse<TimesheetEntryDto>
            embedded
            heading="Timesheet entries"
            showHeading={false}
            searchPlaceholder="Search timesheet entries..."
            filterKey={[projectId, range.fromUtc, range.toUtc, timesheetBrowseEpoch].join('|')}
            fetchPage={async ({ pageIndex, pageSize, search, status, signal }) =>
              api.getProjectTimesheetEntriesPaged(
                projectId!,
                {
                  fromUtc: range.fromUtc,
                  toUtc: range.toUtc,
                  pageIndex,
                  pageSize,
                  search: search || undefined,
                  openClosed:
                    status === 'Open' ? 'open' : status === 'Closed' ? 'closed' : undefined,
                },
                signal,
              )
            }
            getStatusValue={(entry) => (entry.isOpen ? 'Open' : 'Closed')}
            statusOptions={[
              { value: 'Open', label: 'Open' },
              { value: 'Closed', label: 'Closed' },
            ]}
            exportFilename={`project-${detail.id}-timesheet.xlsx`}
            exportTitle={`${detail.name} · Timesheet`}
            exportColumns={[
              { header: 'Category', key: 'categoryName' },
              { header: 'Started', key: 'startedAtUtc' },
              { header: 'Ended', key: 'endedAtUtc' },
              { header: 'Duration (ms)', key: 'durationMilliseconds' },
              { header: 'Notes', key: 'notes' },
              { header: 'Status', key: 'status' },
            ]}
            toExportRow={(entry) => ({
              categoryName: entry.categoryName ?? '',
              startedAtUtc: formatDateTime(entry.startedAtUtc),
              endedAtUtc: formatDateTime(entry.endedAtUtc),
              durationMilliseconds: timesheetDurationMs(entry) ?? '',
              notes: entry.notes ?? '',
              status: entry.isOpen ? 'Open' : 'Closed',
            })}
            emptySourceMessage="No timesheet entries in the selected range."
            renderTable={(rows) => (
              <table className="data">
                <thead>
                  <tr>
                    <th>Category</th>
                    <th>Started</th>
                    <th>Ended</th>
                    <th>Duration</th>
                    <th>Notes</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((entry) => {
                    const duration = timesheetDurationMs(entry);
                    return (
                      <tr key={entry.id}>
                        <td>{entry.categoryName?.trim() ? entry.categoryName : '—'}</td>
                        <td>{formatDateTime(entry.startedAtUtc)}</td>
                        <td>{formatDateTime(entry.endedAtUtc)}</td>
                        <td>
                          {duration == null
                            ? '—'
                            : `${formatDurationMs(duration)}${entry.isOpen ? ' (running)' : ''}`}
                        </td>
                        <td>{entry.notes?.trim() ? entry.notes : '—'}</td>
                        <td>
                          <StatusBadge
                            label={entry.isOpen ? 'Open' : 'Closed'}
                            tone={entry.isOpen ? 'success' : 'neutral'}
                          />
                        </td>
                        <td>
                          <div className="row-actions">
                            <button
                              type="button"
                              className="btn btn-compact btn-secondary"
                              onClick={() => {
                                setEditingTimesheetId(entry.id);
                                setTimesheetDraft(draftFromTimesheet(entry));
                                setTimesheetMessage(null);
                                setTimesheetEditorOpen(true);
                              }}
                            >
                              Edit
                            </button>
                            <button
                              type="button"
                              className="btn btn-compact btn-danger"
                              disabled={deleteTimesheetMutation.isPending}
                              onClick={() => {
                                const ok = window.confirm('Delete this timesheet entry?');
                                if (!ok) return;
                                void deleteTimesheetMutation
                                  .mutateAsync({ id: entry.id, projectId: detail.id })
                                  .then(() => {
                                    setTimesheetMessage(null);
                                    setTimesheetBrowseEpoch((value) => value + 1);
                                  })
                                  .catch((err: unknown) => {
                                    setTimesheetMessage(
                                      err instanceof Error ? err.message : 'Delete failed',
                                    );
                                  });
                              }}
                            >
                              Delete
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
            renderGrid={(rows) =>
              rows.map((entry) => {
                const duration = timesheetDurationMs(entry);
                return (
                <article key={entry.id} className="analysis-browse-tile">
                  <strong>
                    {entry.categoryName?.trim() ? entry.categoryName : 'Uncategorized'}
                  </strong>
                  <span>{formatDateTime(entry.startedAtUtc)}</span>
                  <span>{entry.isOpen ? 'Open' : formatDateTime(entry.endedAtUtc)}</span>
                  <span>
                    {duration == null
                      ? '—'
                      : `${formatDurationMs(duration)}${entry.isOpen ? ' (running)' : ''}`}
                  </span>
                  <span>{entry.notes?.trim() ? entry.notes : 'No notes'}</span>
                  <div className="row-actions">
                    <button
                      type="button"
                      className="btn btn-compact btn-secondary"
                      onClick={() => {
                        setEditingTimesheetId(entry.id);
                        setTimesheetDraft(draftFromTimesheet(entry));
                        setTimesheetMessage(null);
                        setTimesheetEditorOpen(true);
                      }}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      className="btn btn-compact btn-danger"
                      disabled={deleteTimesheetMutation.isPending}
                      onClick={() => {
                        const ok = window.confirm('Delete this timesheet entry?');
                        if (!ok) return;
                        void deleteTimesheetMutation
                          .mutateAsync({ id: entry.id, projectId: detail.id })
                          .then(() => {
                            setTimesheetMessage(null);
                            setTimesheetBrowseEpoch((value) => value + 1);
                          })
                          .catch((err: unknown) => {
                            setTimesheetMessage(
                              err instanceof Error ? err.message : 'Delete failed',
                            );
                          });
                      }}
                    >
                      Delete
                    </button>
                  </div>
                </article>
                );
              })
            }
          />
        </section>
      )}

      {tab === 'Usage' && (
        <section className="page-section">
          {usage.isLoading ? (
            <LoadingState />
          ) : usage.error ? (
            <ErrorState message={usage.error instanceof Error ? usage.error.message : 'Failed'} />
          ) : (
            <div className="metric-grid">
              <MetricCard label="Total tokens" value={formatNumber(usage.data?.totalTokens)} />
              <MetricCard label="Input tokens" value={formatNumber(usage.data?.inputTokens)} />
              <MetricCard label="Output tokens" value={formatNumber(usage.data?.outputTokens)} />
              <MetricCard label="Cached input" value={formatNumber(usage.data?.cachedInputTokens)} />
              <MetricCard label="Reasoning" value={formatNumber(usage.data?.reasoningTokens)} />
              <MetricCard
                label="Reported cost"
                value={formatCurrency(usage.data?.reportedCost, usage.data?.currency)}
              />
              <MetricCard label="Requests" value={formatNumber(usage.data?.requestCount)} />
            </div>
          )}
        </section>
      )}

      {tab === 'Cost' && (
        <section className="page-section">
          {cost.isLoading ? (
            <LoadingState />
          ) : cost.error ? (
            <ErrorState message={cost.error instanceof Error ? cost.error.message : 'Failed'} />
          ) : (
            <>
              <div className="metric-grid">
                <MetricCard
                  label="Usage-based"
                  value={formatCurrency(cost.data?.usageBasedCursorCost, cost.data?.currency)}
                />
                <MetricCard
                  label="Subscription allocation"
                  value={formatCurrency(cost.data?.subscriptionAllocation, cost.data?.currency)}
                />
                <MetricCard
                  label="Other providers"
                  value={formatCurrency(cost.data?.otherProviderCost, cost.data?.currency)}
                />
                <MetricCard
                  label="Unallocated"
                  value={formatCurrency(cost.data?.unallocatedCost, cost.data?.currency)}
                />
                <MetricCard
                  label="Total AI cost"
                  value={formatCurrency(cost.data?.totalAiCost, cost.data?.currency)}
                />
                <MetricCard
                  label="Calculated token cost"
                  value={formatCurrency(cost.data?.calculatedTokenCost ?? 0, cost.data?.currency)}
                  hint="Settings rate card × attributed tokens"
                />
              </div>
              <AnalysisDetailBrowse
                embedded
                heading="Cost by model"
                searchPlaceholder="Search models..."
                rows={cost.data?.byModel ?? []}
                getSearchText={(m) =>
                  [m.name, m.promptCount, m.usageBasedCost, m.calculatedTokenCost]
                    .map(String)
                    .join(' ')
                }
                exportFilename={`project-${detail.id}-cost-by-model.xlsx`}
                exportTitle={`${detail.name} · Cost by model`}
                exportColumns={[
                  { header: 'Model', key: 'name' },
                  { header: 'Usage cost', key: 'usageBasedCost' },
                  { header: 'Subscription', key: 'subscriptionAllocation' },
                  { header: 'Token cost', key: 'calculatedTokenCost' },
                  { header: 'Prompts', key: 'promptCount' },
                ]}
                toExportRow={(m) => ({
                  name: m.name,
                  usageBasedCost: m.usageBasedCost,
                  subscriptionAllocation: m.subscriptionAllocation,
                  calculatedTokenCost: m.calculatedTokenCost ?? 0,
                  promptCount: m.promptCount,
                })}
                emptySourceMessage="No model cost breakdown in this range."
                renderTable={(rows) => (
                  <table className="data">
                    <thead>
                      <tr>
                        <th>Model</th>
                        <th>Usage cost</th>
                        <th>Subscription</th>
                        <th>Token cost</th>
                        <th>Prompts</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((m) => (
                        <tr key={m.name}>
                          <td>{m.name}</td>
                          <td>{formatCurrency(m.usageBasedCost, cost.data?.currency)}</td>
                          <td>
                            {formatCurrency(m.subscriptionAllocation, cost.data?.currency)}
                          </td>
                          <td>
                            {formatCurrency(m.calculatedTokenCost ?? 0, cost.data?.currency)}
                          </td>
                          <td>{formatNumber(m.promptCount)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                renderGrid={(rows) =>
                  rows.map((m) => (
                    <article key={m.name} className="analysis-browse-tile">
                      <strong>{m.name}</strong>
                      <span>
                        Usage {formatCurrency(m.usageBasedCost, cost.data?.currency)}
                      </span>
                      <span>
                        Token{' '}
                        {formatCurrency(m.calculatedTokenCost ?? 0, cost.data?.currency)}
                      </span>
                      <span>Prompts {formatNumber(m.promptCount)}</span>
                    </article>
                  ))
                }
              />
            </>
          )}
        </section>
      )}

      {tab === 'Token Costs' && (
        <section className="page-section">
          {tokenCost.isLoading ? (
            <LoadingState label="Calculating token costs…" />
          ) : tokenCost.error ? (
            <ErrorState
              message={
                tokenCost.error instanceof Error
                  ? tokenCost.error.message
                  : 'Failed to load token costs'
              }
            />
          ) : (
            <>
              <p className="muted" style={{ marginBottom: '1rem' }}>
                Estimated from attributed tokens using the Cursor rate card in Settings. Cached
                tokens use the cache-read rate. Reported cost is the imported dollar amount when
                present.
              </p>
              <div className="metric-grid">
                <MetricCard
                  label="Estimated cost"
                  value={formatCurrency(
                    tokenCost.data?.estimatedCost,
                    tokenCost.data?.currency,
                  )}
                />
                <MetricCard
                  label="Reported cost"
                  value={formatCurrency(
                    tokenCost.data?.reportedCost,
                    tokenCost.data?.currency,
                  )}
                />
                <MetricCard
                  label="Total tokens"
                  value={formatNumber(tokenCost.data?.totalTokens)}
                />
                <MetricCard
                  label="Input tokens"
                  value={formatNumber(tokenCost.data?.inputTokens)}
                />
                <MetricCard
                  label="Output tokens"
                  value={formatNumber(tokenCost.data?.outputTokens)}
                />
                <MetricCard
                  label="Cached input"
                  value={formatNumber(tokenCost.data?.cachedInputTokens)}
                />
                <MetricCard
                  label="Reasoning"
                  value={formatNumber(tokenCost.data?.reasoningTokens)}
                />
                <MetricCard
                  label="Rate card models"
                  value={formatNumber(tokenCost.data?.rateCardModelCount)}
                />
              </div>
              <AnalysisDetailBrowse
                embedded
                heading="Token cost by model"
                searchPlaceholder="Search models..."
                rows={tokenCost.data?.byModel ?? []}
                getSearchText={(row) =>
                  [row.model, row.rateSource, row.totalTokens, row.estimatedCost]
                    .map(String)
                    .join(' ')
                }
                exportFilename={`project-${detail.id}-token-costs.xlsx`}
                exportTitle={`${detail.name} · Token costs`}
                exportColumns={[
                  { header: 'Model', key: 'model' },
                  { header: 'Rate used', key: 'rateSource' },
                  { header: 'Input', key: 'inputTokens' },
                  { header: 'Output', key: 'outputTokens' },
                  { header: 'Cached', key: 'cachedInputTokens' },
                  { header: 'Reasoning', key: 'reasoningTokens' },
                  { header: 'Total tokens', key: 'totalTokens' },
                  { header: 'Estimated', key: 'estimatedCost' },
                  { header: 'Reported', key: 'reportedCost' },
                ]}
                toExportRow={(row) => ({
                  model: row.model,
                  rateSource: row.rateSource,
                  inputTokens: row.inputTokens,
                  outputTokens: row.outputTokens,
                  cachedInputTokens: row.cachedInputTokens,
                  reasoningTokens: row.reasoningTokens,
                  totalTokens: row.totalTokens,
                  estimatedCost: row.estimatedCost,
                  reportedCost: row.reportedCost,
                })}
                emptySourceMessage="No attributed usage in this range to price."
                renderTable={(rows) => (
                  <table className="data">
                    <thead>
                      <tr>
                        <th>Model</th>
                        <th>Rate used</th>
                        <th>Input</th>
                        <th>Output</th>
                        <th>Cached</th>
                        <th>Reasoning</th>
                        <th>Total tokens</th>
                        <th>Estimated</th>
                        <th>Reported</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((row) => (
                        <tr key={row.model}>
                          <td>{row.model}</td>
                          <td className="mono">{row.rateSource}</td>
                          <td>{formatNumber(row.inputTokens)}</td>
                          <td>{formatNumber(row.outputTokens)}</td>
                          <td>{formatNumber(row.cachedInputTokens)}</td>
                          <td>{formatNumber(row.reasoningTokens)}</td>
                          <td>{formatNumber(row.totalTokens)}</td>
                          <td>
                            {formatCurrency(row.estimatedCost, tokenCost.data?.currency)}
                          </td>
                          <td>
                            {formatCurrency(row.reportedCost, tokenCost.data?.currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                renderGrid={(rows) =>
                  rows.map((row) => (
                    <article key={row.model} className="analysis-browse-tile">
                      <strong>{row.model}</strong>
                      <span className="mono">{row.rateSource}</span>
                      <span>Tokens {formatNumber(row.totalTokens)}</span>
                      <span>
                        Est. {formatCurrency(row.estimatedCost, tokenCost.data?.currency)}
                      </span>
                    </article>
                  ))
                }
              />
              <p className="muted" style={{ marginTop: '0.75rem' }}>
                <TextLink to="/settings?tab=cursor-token-costs">
                  Edit Cursor token rates in Settings
                </TextLink>
              </p>
            </>
          )}
        </section>
      )}

      {tab === 'Repositories' && (
        <AnalysisDetailBrowse
          heading="Repositories"
          searchPlaceholder="Search repositories..."
          rows={detail.repositories ?? []}
          getStatusValue={(repo) => (repo.isActive ? 'Active' : 'Inactive')}
          statusOptions={[
            { value: 'Active', label: 'Active' },
            { value: 'Inactive', label: 'Inactive' },
          ]}
          getSearchText={(repo) =>
            [
              repo.localPath,
              repo.remoteUrl,
              repo.defaultBranch,
              repo.isActive ? 'Active' : 'Inactive',
            ]
              .filter(Boolean)
              .join(' ')
          }
          exportFilename={`project-${detail.id}-repositories.xlsx`}
          exportTitle={`${detail.name} · Repositories`}
          exportColumns={[
            { header: 'Local path', key: 'localPath' },
            { header: 'Remote', key: 'remoteUrl' },
            { header: 'Default branch', key: 'defaultBranch' },
            { header: 'Status', key: 'status' },
          ]}
          toExportRow={(repo) => ({
            localPath: repo.localPath,
            remoteUrl: repo.remoteUrl ?? '',
            defaultBranch: repo.defaultBranch ?? '',
            status: repo.isActive ? 'Active' : 'Inactive',
          })}
          emptySourceMessage="No repositories mapped to this project."
          renderTable={(rows) => (
            <table className="data">
              <thead>
                <tr>
                  <th>Local path</th>
                  <th>Remote</th>
                  <th>Default branch</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((repo) => (
                  <tr key={repo.id}>
                    <td className="mono">{repo.localPath}</td>
                    <td className="mono">{repo.remoteUrl ?? '—'}</td>
                    <td>{repo.defaultBranch ?? '—'}</td>
                    <td>
                      <StatusBadge
                        label={repo.isActive ? 'Active' : 'Inactive'}
                        tone={repo.isActive ? 'success' : 'neutral'}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          renderGrid={(rows) =>
            rows.map((repo) => (
              <article key={repo.id} className="analysis-browse-tile">
                <strong className="mono">{repo.localPath}</strong>
                <span className="mono">{repo.remoteUrl ?? 'No remote'}</span>
                <span>{repo.defaultBranch ?? 'No default branch'}</span>
                <span>{repo.isActive ? 'Active' : 'Inactive'}</span>
              </article>
            ))
          }
        />
      )}

      {tab === 'Exports' && (
        <section className="page-section">
          <Panel className="stack">
            <p>Download a project report as JSON or CSV.</p>
            <div className="row">
              <button
                type="button"
                className="btn"
                disabled={exportMutation.isPending}
                onClick={() =>
                  exportMutation.mutate({
                    reportType: 'project',
                    format: 'Json',
                    projectId: detail.id,
                    fromUtc: range.fromUtc,
                    toUtc: range.toUtc,
                    includeActivity: true,
                    includeUsage: true,
                    includeCosts: true,
                  })
                }
              >
                Export JSON
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={exportMutation.isPending}
                onClick={() =>
                  exportMutation.mutate({
                    reportType: 'project',
                    format: 'Csv',
                    projectId: detail.id,
                    fromUtc: range.fromUtc,
                    toUtc: range.toUtc,
                    includeActivity: true,
                    includeUsage: true,
                    includeCosts: true,
                  })
                }
              >
                Export CSV
              </button>
            </div>
            {exportMutation.isSuccess ? (
              <p className="mono">
                Downloaded {exportMutation.data.fileName} (
                {formatNumber(exportMutation.data.byteCount)} bytes)
              </p>
            ) : null}
            {exportMutation.isError ? (
              <ErrorState
                message={
                  exportMutation.error instanceof Error
                    ? exportMutation.error.message
                    : 'Export failed'
                }
              />
            ) : null}
          </Panel>
        </section>
      )}

      {tab === 'Settings' && (
        <section className="page-section">
          <Panel className="stack"><form
            className="stack"
            onSubmit={async (event) => {
              event.preventDefault();
              setSettingsMessage(null);
              try {
                await updateMutation.mutateAsync({
                  id: detail.id,
                  body: {
                    name: settingsDraft.name.trim(),
                    slug: settingsDraft.slug.trim() || null,
                    clientName: settingsDraft.clientName || null,
                    billingCode: settingsDraft.billingCode || null,
                    currency: settingsDraft.currency,
                    isActive: settingsDraft.isActive,
                  },
                });
                setSettingsMessage('Project settings saved.');
                await project.refetch();
              } catch (err) {
                setSettingsMessage(err instanceof Error ? err.message : 'Save failed');
              }
            }}
          >
            <div className="field-row">
              <div className="field">
                <label htmlFor="name">Name</label>
                <input
                  id="name"
                  required
                  value={settingsDraft.name}
                  onChange={(e) => setSettingsDraft((s) => ({ ...s, name: e.target.value }))}
                />
              </div>
              <div className="field">
                <label htmlFor="slug">Slug</label>
                <input
                  id="slug"
                  value={settingsDraft.slug}
                  onChange={(e) => setSettingsDraft((s) => ({ ...s, slug: e.target.value }))}
                />
              </div>
              <div className="field">
                <label htmlFor="clientName">Client</label>
                <input
                  id="clientName"
                  value={settingsDraft.clientName}
                  onChange={(e) => setSettingsDraft((s) => ({ ...s, clientName: e.target.value }))}
                />
              </div>
              <div className="field">
                <label htmlFor="billingCode">Billing code</label>
                <input
                  id="billingCode"
                  value={settingsDraft.billingCode}
                  onChange={(e) => setSettingsDraft((s) => ({ ...s, billingCode: e.target.value }))}
                />
              </div>
              <div className="field">
                <label htmlFor="currency">Currency</label>
                <input
                  id="currency"
                  value={settingsDraft.currency}
                  onChange={(e) => setSettingsDraft((s) => ({ ...s, currency: e.target.value }))}
                />
              </div>
            </div>
            <label className="row">
              <input
                type="checkbox"
                checked={settingsDraft.isActive}
                onChange={(e) => setSettingsDraft((s) => ({ ...s, isActive: e.target.checked }))}
              />
              Project is active
            </label>
            <div className="row-actions">
              <button type="submit" className="btn" disabled={updateMutation.isPending}>
                {updateMutation.isPending ? 'Saving…' : 'Save project settings'}
              </button>
              <button
                type="button"
                className="btn btn-danger"
                disabled={deleteMutation.isPending}
                onClick={() => {
                  const ok = window.confirm(
                    `Delete project “${detail.name}”? It will be deactivated and removed from the active list.`,
                  );
                  if (!ok) {
                    return;
                  }
                  void deleteMutation
                    .mutateAsync(detail.id)
                    .then(() => navigate('/projects'))
                    .catch((err: unknown) => {
                      setSettingsMessage(err instanceof Error ? err.message : 'Delete failed');
                    });
                }}
              >
                Delete project
              </button>
              {settingsMessage ? <span>{settingsMessage}</span> : null}
            </div>
          </form></Panel>
        </section>
      )}
    </Page>
  );
}
