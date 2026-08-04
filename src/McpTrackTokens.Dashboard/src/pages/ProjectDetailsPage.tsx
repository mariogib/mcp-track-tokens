import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  useCreateProjectSessionMutation,
  useCreateTimesheetEntryMutation,
  useDeleteProjectMutation,
  useDeleteSessionMutation,
  useDeleteTimesheetEntryMutation,
  useProjectActivityQuery,
  useProjectCostQuery,
  useProjectTokenCostQuery,
  useProjectPromptFacetsQuery,
  useProjectQuery,
  useProjectSessionsQuery,
  useProjectTimesheetQuery,
  useProjectUsageQuery,
  useRecalculateActivityWindowsMutation,
  useSessionPromptsQuery,
  useTimesheetCategoriesQuery,
  useTimesheetReportMonthsQuery,
  useUpdateProjectMutation,
  useUpdateSessionMutation,
  useUpdateTimesheetEntryMutation,
} from '../api/hooks';
import { api } from '../api/client';
import type {
  DailyActivityRow,
  LinkedPromptSummaryDto,
  PromptEventDto,
  PromptUsageTypeBreakdownDto,
  ProjectUsageEntryDto,
  SessionDto,
  TimesheetEntryDto,
} from '../api/types';
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
import { MetricCard, Panel, TablePanel } from '../components/MetricCard';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';
import { Breadcrumb, DataTable, PopupForm, TextLink } from '../shared/adminUi';
import {
  buildDaySeries,
  buildModelCalculatedSeries,
  buildModelCostSeries,
  resolveDisplayCost,
} from '../utils/chartDetail';
import { browseSortQuery } from '../utils/browseSort';
import {
  sessionDurationMs,
  sessionsWithinTimesheetPeriods,
  sessionsWithinTimeRange,
  timesheetsWithinTimeRange,
  dayBoundsLocal,
  timesheetEntryDurationMs,
} from '../utils/duration';
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
} from '../utils/format';
import { exportProjectDetailsWorkbook } from '../utils/projectDetailsExcelExport';

const PROMPT_TIME_CUSTOM = '__custom__';

type CombinedModelCostRow = {
  model: string;
  promptCount: number;
  usageBasedCost: number;
  subscriptionAllocation: number;
  calculatedTokenCost: number;
  rateSource: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  cacheWriteTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  estimatedCost: number;
  reportedCost: number;
};

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
  'Costs',
  'Settings',
] as const;

/** Old Cost / Token Costs / Repositories / Exports tab URLs map onto current tabs. */
const TAB_ALIASES = {
  cost: 'Costs',
  'token-costs': 'Costs',
  repositories: 'Settings',
  exports: 'Settings',
} as const satisfies Readonly<Record<string, (typeof TABS)[number]>>;

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
  const [tab, setTab] = useTabSearchParam(TABS, 'Overview', 'tab', TAB_ALIASES);
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
    repositoryPath: '',
    remoteUrl: '',
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
  const [selectedPrompt, setSelectedPrompt] = useState<PromptEventDto | null>(null);
  const [selectedSessionForPrompts, setSelectedSessionForPrompts] = useState<SessionDto | null>(
    null,
  );
  const [selectedActivityDayDrilldown, setSelectedActivityDayDrilldown] = useState<{
    day: DailyActivityRow;
    kind: 'sessions' | 'timesheets';
  } | null>(null);
  const [selectedUsage, setSelectedUsage] = useState<ProjectUsageEntryDto | null>(null);
  const [selectedCostModel, setSelectedCostModel] = useState<CombinedModelCostRow | null>(null);
  const [selectedTimesheetEntry, setSelectedTimesheetEntry] = useState<TimesheetEntryDto | null>(
    null,
  );
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

  const detail = project.data;

  useEffect(() => {
    if (tab !== 'Settings' || !detail) {
      return;
    }

    setSettingsDraft({
      name: detail.name,
      slug: detail.slug,
      clientName: detail.clientName ?? '',
      billingCode: detail.billingCode ?? '',
      currency: detail.currency || 'USD',
      repositoryPath: detail.primaryRepositoryPath ?? '',
      remoteUrl: detail.primaryRemoteUrl ?? '',
      isActive: detail.isActive,
    });
    // Depend on stable fields only — `detail` identity changes on refetch and would wipe in-progress edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- sync when persisted project fields change
  }, [
    tab,
    detail?.id,
    detail?.name,
    detail?.slug,
    detail?.clientName,
    detail?.billingCode,
    detail?.currency,
    detail?.primaryRepositoryPath,
    detail?.primaryRemoteUrl,
    detail?.isActive,
  ]);

  if (project.isLoading) return <LoadingState label="Loading project…" />;
  if (project.error || !detail) {
    return (
      <ErrorState
        message={
          project.error instanceof Error ? project.error.message : 'Project not found'
        }
      />
    );
  }

  const byDay = activity.data?.byDay ?? [];
  // Charts stay chronological (oldest → newest); grids use byDay as returned (newest first).
  const byDayChronological = [...byDay].sort((a, b) => a.day.localeCompare(b.day));
  // When Cursor exports are Included/Free, reported totalAiCost is $0 — use rate-card
  // calculatedTokenCost so overview charts stay meaningful.
  const reportedTotalCost = cost.data?.totalAiCost ?? 0;
  const calculatedTotalCost = cost.data?.calculatedTokenCost ?? 0;
  const { displayTotalCost, usingCalculatedCost } = resolveDisplayCost(
    reportedTotalCost,
    calculatedTotalCost,
  );
  const chartDaySeries = buildDaySeries(byDayChronological, displayTotalCost);
  const daySeries = chartDaySeries;
  const costByDay = chartDaySeries;
  const modelCostSeries = buildModelCostSeries(cost.data?.byModel ?? [], '');
  const modelCalculatedSeries = buildModelCalculatedSeries(cost.data?.byModel ?? [], '');

  const combinedModelCostRows = (() => {
    const byKey = new Map<string, CombinedModelCostRow>();

    const keyFor = (name: string) => name.trim().toLowerCase() || 'unknown';

    for (const row of cost.data?.byModel ?? []) {
      const model = row.name || 'Unknown';
      byKey.set(keyFor(model), {
        model,
        promptCount: row.promptCount,
        usageBasedCost: row.usageBasedCost,
        subscriptionAllocation: row.subscriptionAllocation,
        calculatedTokenCost: row.calculatedTokenCost ?? 0,
        rateSource: '—',
        inputTokens: 0,
        outputTokens: 0,
        cachedInputTokens: 0,
        cacheWriteTokens: 0,
        reasoningTokens: 0,
        totalTokens: 0,
        estimatedCost: row.calculatedTokenCost ?? 0,
        reportedCost: 0,
      });
    }

    for (const row of tokenCost.data?.byModel ?? []) {
      const model = row.model || 'Unknown';
      const key = keyFor(model);
      const existing = byKey.get(key);
      if (existing) {
        existing.rateSource = row.rateSource;
        existing.inputTokens = row.inputTokens;
        existing.outputTokens = row.outputTokens;
        existing.cachedInputTokens = row.cachedInputTokens;
        existing.cacheWriteTokens = row.cacheWriteTokens ?? 0;
        existing.reasoningTokens = row.reasoningTokens;
        existing.totalTokens = row.totalTokens;
        existing.estimatedCost = row.estimatedCost;
        existing.reportedCost = row.reportedCost;
        if (!existing.calculatedTokenCost) {
          existing.calculatedTokenCost = row.estimatedCost;
        }
      } else {
        byKey.set(key, {
          model,
          promptCount: 0,
          usageBasedCost: 0,
          subscriptionAllocation: 0,
          calculatedTokenCost: row.estimatedCost,
          rateSource: row.rateSource,
          inputTokens: row.inputTokens,
          outputTokens: row.outputTokens,
          cachedInputTokens: row.cachedInputTokens,
          cacheWriteTokens: row.cacheWriteTokens ?? 0,
          reasoningTokens: row.reasoningTokens,
          totalTokens: row.totalTokens,
          estimatedCost: row.estimatedCost,
          reportedCost: row.reportedCost,
        });
      }
    }

    return [...byKey.values()].sort((a, b) => a.model.localeCompare(b.model));
  })();

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
          <>
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
                  row.timesheetEntryCount ?? 0,
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
                { header: 'Timesheets', key: 'timesheetEntryCount' },
                { header: 'Timesheet duration (s)', key: 'timesheetDurationSeconds' },
              ]}
              toExportRow={(row) => ({
                day: formatDay(row.day),
                promptCount: row.promptCount,
                agentRuns: row.agentRuns,
                agentDurationMilliseconds: row.agentDurationMilliseconds,
                activeProjectTimeSeconds: row.activeProjectTimeSeconds,
                sessionCount: row.sessionCount,
                timesheetEntryCount: row.timesheetEntryCount ?? 0,
                timesheetDurationSeconds: row.timesheetDurationSeconds ?? 0,
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
                      <th>Timesheets</th>
                      <th>Timesheet duration</th>
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
                        <td>
                          <TextLink
                            title={`Click to show sessions for ${formatDay(row.day)}`}
                            ariaLabel={`Click to show sessions for ${formatDay(row.day)}`}
                            onClick={() =>
                              setSelectedActivityDayDrilldown({ day: row, kind: 'sessions' })
                            }
                          >
                            {formatNumber(row.sessionCount)}
                          </TextLink>
                        </td>
                        <td>
                          <TextLink
                            title={`Click to show timesheets for ${formatDay(row.day)}`}
                            ariaLabel={`Click to show timesheets for ${formatDay(row.day)}`}
                            onClick={() =>
                              setSelectedActivityDayDrilldown({ day: row, kind: 'timesheets' })
                            }
                          >
                            {formatNumber(row.timesheetEntryCount ?? 0)}
                          </TextLink>
                        </td>
                        <td>
                          <TextLink
                            title={`Click to show timesheets for ${formatDay(row.day)}`}
                            ariaLabel={`Click to show timesheet duration details for ${formatDay(row.day)}`}
                            onClick={() =>
                              setSelectedActivityDayDrilldown({ day: row, kind: 'timesheets' })
                            }
                          >
                            {formatDurationSeconds(row.timesheetDurationSeconds ?? 0)}
                          </TextLink>
                        </td>
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
                    <TextLink
                      title={`Click to show sessions for ${formatDay(row.day)}`}
                      ariaLabel={`Click to show sessions for ${formatDay(row.day)}`}
                      onClick={() =>
                        setSelectedActivityDayDrilldown({ day: row, kind: 'sessions' })
                      }
                    >
                      Sessions {formatNumber(row.sessionCount)}
                    </TextLink>
                    <TextLink
                      title={`Click to show timesheets for ${formatDay(row.day)}`}
                      ariaLabel={`Click to show timesheets for ${formatDay(row.day)}`}
                      onClick={() =>
                        setSelectedActivityDayDrilldown({ day: row, kind: 'timesheets' })
                      }
                    >
                      Timesheets {formatNumber(row.timesheetEntryCount ?? 0)}
                    </TextLink>
                    <TextLink
                      title={`Click to show timesheets for ${formatDay(row.day)}`}
                      ariaLabel={`Click to show timesheet duration details for ${formatDay(row.day)}`}
                      onClick={() =>
                        setSelectedActivityDayDrilldown({ day: row, kind: 'timesheets' })
                      }
                    >
                      Timesheet {formatDurationSeconds(row.timesheetDurationSeconds ?? 0)}
                    </TextLink>
                  </article>
                ))
              }
            />
            {selectedActivityDayDrilldown && projectId ? (
              selectedActivityDayDrilldown.kind === 'sessions' ? (
                <ActivityDaySessionsDialog
                  day={selectedActivityDayDrilldown.day.day}
                  projectId={projectId}
                  onClose={() => setSelectedActivityDayDrilldown(null)}
                />
              ) : (
                <ActivityDayTimesheetsDialog
                  day={selectedActivityDayDrilldown.day.day}
                  projectId={projectId}
                  onClose={() => setSelectedActivityDayDrilldown(null)}
                />
              )
            ) : null}
          </>
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
            fetchPage={async ({ pageIndex, pageSize, search, status, sort, signal }) =>
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
                  ...browseSortQuery(sort),
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
            renderTable={(rows, { sort, onSortChange }) => (
              <DataTable
                className="data"
                shellClassName=""
                sort={sort}
                onSortChange={onSortChange}
                headers={[
                  { id: 'timestampUtc', header: 'Time', sortable: true },
                  { id: 'eventType', header: 'Type', sortable: true },
                  { id: 'editor', header: 'Editor', sortable: true },
                  { id: 'model', header: 'Model', sortable: true },
                  { id: 'branch', header: 'Branch', sortable: true },
                  { id: 'status', header: 'Status', sortable: true },
                  { id: 'durationMilliseconds', header: 'Duration', sortable: true },
                  { id: 'linkedUsageCount', header: 'Linked usages' },
                  { id: 'totalTokens', header: 'Total Tokens' },
                  { id: 'reportedCost', header: 'Cost' },
                  { id: 'calculatedTokenCost', header: 'Calculated cost' },
                ]}
              >
                {rows.map((p) => (
                  <tr
                    key={p.id}
                    className="clickable-row"
                    tabIndex={0}
                    role="button"
                    title="Click to show usage breakdown for this prompt"
                    aria-label={`Show usage breakdown for prompt at ${formatDateTime(p.timestampUtc)}`}
                    onClick={() => setSelectedPrompt(p)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        setSelectedPrompt(p);
                      }
                    }}
                  >
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
              </DataTable>
            )}
            renderGrid={(rows) =>
              rows.map((p) => (
                <article
                  key={p.id}
                  className="analysis-browse-tile clickable-tile"
                  tabIndex={0}
                  role="button"
                  title="Click to show usage breakdown for this prompt"
                  aria-label={`Show usage breakdown for prompt at ${formatDateTime(p.timestampUtc)}`}
                  onClick={() => setSelectedPrompt(p)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      setSelectedPrompt(p);
                    }
                  }}
                >
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

      {selectedPrompt ? (
        <PromptUsageBreakdownDialog
          prompt={selectedPrompt}
          onClose={() => setSelectedPrompt(null)}
        />
      ) : null}

      {tab === 'Sessions' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Sessions</h2>
              <p className="muted">
                Add, edit, or delete tracked editor sessions for this project. Click a row to see
                prompts in that session.
              </p>
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
            fetchPage={async ({ pageIndex, pageSize, search, status, sort, signal }) =>
              api.getProjectSessionsPaged(
                projectId!,
                {
                  fromUtc: range.fromUtc,
                  toUtc: range.toUtc,
                  pageIndex,
                  pageSize,
                  search: search || undefined,
                  status: status || undefined,
                  ...browseSortQuery(sort),
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
            renderTable={(rows, { sort, onSortChange }) => (
              <DataTable
                className="data"
                shellClassName=""
                sort={sort}
                onSortChange={onSortChange}
                headers={[
                  { id: 'id', header: 'Session', sortable: true },
                  { id: 'editor', header: 'Editor', sortable: true },
                  { id: 'startedAtUtc', header: 'Started', sortable: true },
                  { id: 'endedAtUtc', header: 'Ended', sortable: true },
                  { id: 'durationMilliseconds', header: 'Duration' },
                  { id: 'branch', header: 'Branch', sortable: true },
                  { id: 'status', header: 'Status', sortable: true },
                  { id: 'actions', header: 'Actions' },
                ]}
              >
                {rows.map((s) => {
                  const durationMs = sessionDurationMs(s);
                  return (
                    <tr
                      key={s.id}
                      className="clickable-row"
                      onClick={() => setSelectedSessionForPrompts(s)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          setSelectedSessionForPrompts(s);
                        }
                      }}
                      role="button"
                      tabIndex={0}
                      title="Click to show prompts in this session"
                      aria-label={`Show prompts for session ${s.id.slice(0, 8)}`}
                    >
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
                        <div className="row-actions" onClick={(e) => e.stopPropagation()}>
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
              </DataTable>
            )}
            renderGrid={(rows) =>
              rows.map((s) => {
                const durationMs = sessionDurationMs(s);
                return (
                  <article
                    key={s.id}
                    className="analysis-browse-tile clickable-row"
                    onClick={() => setSelectedSessionForPrompts(s)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        setSelectedSessionForPrompts(s);
                      }
                    }}
                    role="button"
                    tabIndex={0}
                    title="Click to show prompts in this session"
                    aria-label={`Show prompts for session ${s.id.slice(0, 8)}`}
                  >
                    <strong className="mono">{s.id.slice(0, 8)}</strong>
                    <span>{s.editor ?? '—'}</span>
                    <span>{formatDateTime(s.startedAtUtc)}</span>
                    <span>
                      {durationMs == null ? '—' : formatDurationMs(durationMs)}
                    </span>
                    <span>{s.branch ?? 'No branch'}</span>
                    <div className="row-actions" onClick={(e) => e.stopPropagation()}>
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

          {selectedSessionForPrompts ? (
            <SessionPromptsDialog
              session={selectedSessionForPrompts}
              onClose={() => setSelectedSessionForPrompts(null)}
            />
          ) : null}
        </section>
      )}

      {tab === 'Timesheet' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Timesheet</h2>
              <p className="muted">
                Capture billable time with category, start, end, and notes. Click a row to see
                sessions that fall within that timesheet period. MCP tools{' '}
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
            fetchPage={async ({ pageIndex, pageSize, search, status, sort, signal }) =>
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
                  ...browseSortQuery(sort),
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
              durationMilliseconds: timesheetEntryDurationMs(entry) ?? '',
              notes: entry.notes ?? '',
              status: entry.isOpen ? 'Open' : 'Closed',
            })}
            emptySourceMessage="No timesheet entries in the selected range."
            renderTable={(rows, { sort, onSortChange }) => (
              <DataTable
                className="data"
                shellClassName=""
                sort={sort}
                onSortChange={onSortChange}
                headers={[
                  { id: 'categoryName', header: 'Category', sortable: true },
                  { id: 'startedAtUtc', header: 'Started', sortable: true },
                  { id: 'endedAtUtc', header: 'Ended', sortable: true },
                  { id: 'durationMilliseconds', header: 'Duration' },
                  { id: 'notes', header: 'Notes', sortable: true },
                  { id: 'status', header: 'Status', sortable: true },
                  { id: 'actions', header: 'Actions' },
                ]}
              >
                {rows.map((entry) => {
                  const duration = timesheetEntryDurationMs(entry);
                  return (
                    <tr
                      key={entry.id}
                      className="clickable-row"
                      onClick={() => setSelectedTimesheetEntry(entry)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          setSelectedTimesheetEntry(entry);
                        }
                      }}
                      role="button"
                      tabIndex={0}
                      title="Click to show sessions in this timesheet period"
                      aria-label={`Show sessions for timesheet starting ${formatDateTime(entry.startedAtUtc)}`}
                    >
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
                        <div className="row-actions" onClick={(e) => e.stopPropagation()}>
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
              </DataTable>
            )}
            renderGrid={(rows) =>
              rows.map((entry) => {
                const duration = timesheetEntryDurationMs(entry);
                return (
                <article
                  key={entry.id}
                  className="analysis-browse-tile clickable-row"
                  onClick={() => setSelectedTimesheetEntry(entry)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      setSelectedTimesheetEntry(entry);
                    }
                  }}
                  role="button"
                  tabIndex={0}
                  title="Click to show sessions in this timesheet period"
                  aria-label={`Show sessions for timesheet starting ${formatDateTime(entry.startedAtUtc)}`}
                >
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
                  <div className="row-actions" onClick={(e) => e.stopPropagation()}>
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

          {selectedTimesheetEntry ? (
            <TimesheetSessionsDialog
              entry={selectedTimesheetEntry}
              projectId={detail.id}
              onClose={() => setSelectedTimesheetEntry(null)}
            />
          ) : null}
        </section>
      )}

      {tab === 'Usage' && (
        <section className="page-section stack">
          {usage.isLoading ? (
            <LoadingState />
          ) : usage.error ? (
            <ErrorState message={usage.error instanceof Error ? usage.error.message : 'Failed'} />
          ) : (
            <>
              <div className="metric-grid">
                <MetricCard label="Total tokens" value={formatNumber(usage.data?.totalTokens)} />
                <MetricCard label="Input tokens" value={formatNumber(usage.data?.inputTokens)} />
                <MetricCard label="Output tokens" value={formatNumber(usage.data?.outputTokens)} />
                <MetricCard label="Cached input" value={formatNumber(usage.data?.cachedInputTokens)} />
                <MetricCard
                  label="Cache write"
                  value={formatNumber(usage.data?.cacheWriteTokens ?? 0)}
                />
                <MetricCard label="Reasoning" value={formatNumber(usage.data?.reasoningTokens)} />
                <MetricCard label="Requests" value={formatNumber(usage.data?.requestCount)} />
                <MetricCard
                  label="Total calculated cost"
                  value={formatCurrency(
                    usage.data?.calculatedTokenCost ?? 0,
                    usage.data?.currency ?? detail.currency,
                  )}
                  hint="Settings rate card × attributed tokens"
                />
              </div>
              <AnalysisDetailBrowse
                embedded
                heading="Usage entries"
                searchPlaceholder="Search models..."
                rows={usage.data?.items ?? []}
                getSearchText={(row) =>
                  [
                    row.model,
                    row.timestampUtc,
                    row.inputTokens,
                    row.outputTokens,
                    row.cachedInputTokens,
                    row.cacheWriteTokens,
                    row.reasoningTokens,
                    row.totalTokens,
                    row.calculatedTokenCost,
                  ]
                    .map(String)
                    .join(' ')
                }
                exportFilename={`project-${detail.id}-usage-entries.xlsx`}
                exportTitle={`${detail.name} · Usage entries`}
                exportColumns={[
                  { header: 'When', key: 'timestampUtc' },
                  { header: 'Model', key: 'model' },
                  { header: 'Input', key: 'inputTokens' },
                  { header: 'Output', key: 'outputTokens' },
                  { header: 'Cached', key: 'cachedInputTokens' },
                  { header: 'Cache write', key: 'cacheWriteTokens' },
                  { header: 'Reasoning', key: 'reasoningTokens' },
                  { header: 'Total tokens', key: 'totalTokens' },
                  { header: 'Calculated cost', key: 'calculatedTokenCost' },
                ]}
                toExportRow={(row) => ({
                  timestampUtc: formatDateTime(row.timestampUtc),
                  model: row.model ?? '',
                  inputTokens: row.inputTokens,
                  outputTokens: row.outputTokens,
                  cachedInputTokens: row.cachedInputTokens,
                  cacheWriteTokens: row.cacheWriteTokens ?? 0,
                  reasoningTokens: row.reasoningTokens,
                  totalTokens: row.totalTokens,
                  calculatedTokenCost: row.calculatedTokenCost,
                })}
                emptySourceMessage="No attributed usage entries in this range."
                renderTable={(rows) => {
                  const currency = usage.data?.currency ?? detail.currency;
                  return (
                    <table className="data">
                      <thead>
                        <tr>
                          <th>When</th>
                          <th>Model</th>
                          <th>Input</th>
                          <th>Output</th>
                          <th>Cached</th>
                          <th>Cache write</th>
                          <th>Reasoning</th>
                          <th>Total tokens</th>
                          <th>Calculated cost</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map((row) => (
                          <tr
                            key={row.usageRecordId}
                            className="clickable-row"
                            tabIndex={0}
                            role="button"
                            title="Click to show the linked prompt for this usage entry"
                            aria-label={`Show linked prompt for usage at ${formatDateTime(row.timestampUtc)}`}
                            onClick={() => setSelectedUsage(row)}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                setSelectedUsage(row);
                              }
                            }}
                          >
                            <td>{formatDateTime(row.timestampUtc)}</td>
                            <td>{row.model ?? '—'}</td>
                            <td>{formatNumber(row.inputTokens)}</td>
                            <td>{formatNumber(row.outputTokens)}</td>
                            <td>{formatNumber(row.cachedInputTokens)}</td>
                            <td>{formatNumber(row.cacheWriteTokens ?? 0)}</td>
                            <td>{formatNumber(row.reasoningTokens)}</td>
                            <td>{formatNumber(row.totalTokens)}</td>
                            <td>{formatCurrency(row.calculatedTokenCost, currency)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  );
                }}
                renderGrid={(rows) => {
                  const currency = usage.data?.currency ?? detail.currency;
                  return rows.map((row) => (
                    <article
                      key={row.usageRecordId}
                      className="analysis-browse-tile clickable-tile"
                      tabIndex={0}
                      role="button"
                      title="Click to show the linked prompt for this usage entry"
                      aria-label={`Show linked prompt for usage at ${formatDateTime(row.timestampUtc)}`}
                      onClick={() => setSelectedUsage(row)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          setSelectedUsage(row);
                        }
                      }}
                    >
                      <strong>{row.model ?? 'Unknown model'}</strong>
                      <span>{formatDateTime(row.timestampUtc)}</span>
                      <span>
                        In {formatNumber(row.inputTokens)} · Out {formatNumber(row.outputTokens)}
                      </span>
                      <span>
                        Cached {formatNumber(row.cachedInputTokens)} · Write{' '}
                        {formatNumber(row.cacheWriteTokens ?? 0)} · Reasoning{' '}
                        {formatNumber(row.reasoningTokens)}
                      </span>
                      <span>Total {formatNumber(row.totalTokens)}</span>
                      <span>{formatCurrency(row.calculatedTokenCost, currency)}</span>
                    </article>
                  ));
                }}
              />
            </>
          )}
        </section>
      )}

      {selectedUsage ? (
        <LinkedPromptDialog usage={selectedUsage} onClose={() => setSelectedUsage(null)} />
      ) : null}

      {tab === 'Costs' && (
        <section className="page-section stack">
          {cost.isLoading || tokenCost.isLoading ? (
            <LoadingState label="Loading costs…" />
          ) : cost.error || tokenCost.error ? (
            <ErrorState
              message={
                (cost.error instanceof Error
                  ? cost.error.message
                  : null) ??
                (tokenCost.error instanceof Error
                  ? tokenCost.error.message
                  : null) ??
                'Failed to load costs'
              }
            />
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
                <MetricCard
                  label="Estimated cost"
                  value={formatCurrency(
                    tokenCost.data?.estimatedCost,
                    tokenCost.data?.currency ?? cost.data?.currency,
                  )}
                />
                <MetricCard
                  label="Reported cost"
                  value={formatCurrency(
                    tokenCost.data?.reportedCost,
                    tokenCost.data?.currency ?? cost.data?.currency,
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
                  label="Cache write"
                  value={formatNumber(tokenCost.data?.cacheWriteTokens ?? 0)}
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
              <p className="muted">
                Estimated from attributed tokens using the Cursor rate card in Settings. Cached
                input uses the cache-read rate; cache-write tokens use the cache-write rate.
                Reported cost is the imported dollar amount when present.
              </p>
              <AnalysisDetailBrowse
                embedded
                heading="Cost by model"
                searchPlaceholder="Search models..."
                rows={combinedModelCostRows}
                getSearchText={(row) =>
                  [
                    row.model,
                    row.rateSource,
                    row.promptCount,
                    row.totalTokens,
                    row.usageBasedCost,
                    row.calculatedTokenCost,
                    row.estimatedCost,
                    row.reportedCost,
                  ]
                    .map(String)
                    .join(' ')
                }
                exportFilename={`project-${detail.id}-costs-by-model.xlsx`}
                exportTitle={`${detail.name} · Costs by model`}
                exportColumns={[
                  { header: 'Model', key: 'model' },
                  { header: 'Rate used', key: 'rateSource' },
                  { header: 'Prompts', key: 'promptCount' },
                  { header: 'Input', key: 'inputTokens' },
                  { header: 'Output', key: 'outputTokens' },
                  { header: 'Cached', key: 'cachedInputTokens' },
                  { header: 'Cache write', key: 'cacheWriteTokens' },
                  { header: 'Reasoning', key: 'reasoningTokens' },
                  { header: 'Total tokens', key: 'totalTokens' },
                  { header: 'Usage cost', key: 'usageBasedCost' },
                  { header: 'Subscription', key: 'subscriptionAllocation' },
                  { header: 'Calculated cost', key: 'calculatedTokenCost' },
                  { header: 'Estimated', key: 'estimatedCost' },
                  { header: 'Reported', key: 'reportedCost' },
                ]}
                toExportRow={(row) => ({
                  model: row.model,
                  rateSource: row.rateSource === '—' ? '' : row.rateSource,
                  promptCount: row.promptCount,
                  inputTokens: row.inputTokens,
                  outputTokens: row.outputTokens,
                  cachedInputTokens: row.cachedInputTokens,
                  cacheWriteTokens: row.cacheWriteTokens,
                  reasoningTokens: row.reasoningTokens,
                  totalTokens: row.totalTokens,
                  usageBasedCost: row.usageBasedCost,
                  subscriptionAllocation: row.subscriptionAllocation,
                  calculatedTokenCost: row.calculatedTokenCost,
                  estimatedCost: row.estimatedCost,
                  reportedCost: row.reportedCost,
                })}
                emptySourceMessage="No model cost breakdown in this range."
                renderTable={(rows) => {
                  const currency =
                    cost.data?.currency ?? tokenCost.data?.currency ?? detail.currency;
                  return (
                    <table className="data">
                      <thead>
                        <tr>
                          <th>Model</th>
                          <th>Rate used</th>
                          <th>Prompts</th>
                          <th>Input</th>
                          <th>Output</th>
                          <th>Cached</th>
                          <th>Cache write</th>
                          <th>Reasoning</th>
                          <th>Total tokens</th>
                          <th>Usage cost</th>
                          <th>Subscription</th>
                          <th>Calculated cost</th>
                          <th>Estimated</th>
                          <th>Reported</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map((row) => (
                          <tr
                            key={row.model}
                            className="clickable-row"
                            tabIndex={0}
                            role="button"
                            title="Click to show prompts and usage detail for this model"
                            aria-label={`Show prompts and usage types for ${row.model}`}
                            onClick={() => setSelectedCostModel(row)}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                setSelectedCostModel(row);
                              }
                            }}
                          >
                            <td>{row.model}</td>
                            <td className="mono">{row.rateSource}</td>
                            <td>{formatNumber(row.promptCount)}</td>
                            <td>{formatNumber(row.inputTokens)}</td>
                            <td>{formatNumber(row.outputTokens)}</td>
                            <td>{formatNumber(row.cachedInputTokens)}</td>
                            <td>{formatNumber(row.cacheWriteTokens)}</td>
                            <td>{formatNumber(row.reasoningTokens)}</td>
                            <td>{formatNumber(row.totalTokens)}</td>
                            <td>{formatCurrency(row.usageBasedCost, currency)}</td>
                            <td>{formatCurrency(row.subscriptionAllocation, currency)}</td>
                            <td>{formatCurrency(row.calculatedTokenCost, currency)}</td>
                            <td>{formatCurrency(row.estimatedCost, currency)}</td>
                            <td>{formatCurrency(row.reportedCost, currency)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  );
                }}
                renderGrid={(rows) => {
                  const currency =
                    cost.data?.currency ?? tokenCost.data?.currency ?? detail.currency;
                  return rows.map((row) => (
                    <article
                      key={row.model}
                      className="analysis-browse-tile clickable-tile"
                      tabIndex={0}
                      role="button"
                      title="Click to show prompts and usage detail for this model"
                      aria-label={`Show prompts and usage types for ${row.model}`}
                      onClick={() => setSelectedCostModel(row)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          setSelectedCostModel(row);
                        }
                      }}
                    >
                      <strong>{row.model}</strong>
                      <span className="mono">{row.rateSource}</span>
                      <span>Prompts {formatNumber(row.promptCount)}</span>
                      <span>Tokens {formatNumber(row.totalTokens)}</span>
                      <span>Usage {formatCurrency(row.usageBasedCost, currency)}</span>
                      <span>
                        Calculated {formatCurrency(row.calculatedTokenCost, currency)}
                      </span>
                      <span>Est. {formatCurrency(row.estimatedCost, currency)}</span>
                    </article>
                  ));
                }}
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

      {selectedCostModel ? (
        <CostModelDetailDialog
          modelRow={selectedCostModel}
          usageItems={usage.data?.items ?? []}
          currency={cost.data?.currency ?? tokenCost.data?.currency ?? detail.currency}
          onClose={() => setSelectedCostModel(null)}
        />
      ) : null}

      {tab === 'Settings' && (
        <section className="page-section stack">
          <Panel className="stack">
            <form
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
                      repositoryPath: settingsDraft.repositoryPath.trim(),
                      remoteUrl: settingsDraft.remoteUrl.trim(),
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
                    onChange={(e) =>
                      setSettingsDraft((s) => ({ ...s, clientName: e.target.value }))
                    }
                  />
                </div>
                <div className="field">
                  <label htmlFor="billingCode">Billing code</label>
                  <input
                    id="billingCode"
                    value={settingsDraft.billingCode}
                    onChange={(e) =>
                      setSettingsDraft((s) => ({ ...s, billingCode: e.target.value }))
                    }
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
              <div className="field-row">
                <div className="field">
                  <label htmlFor="repositoryPath">Local repository path</label>
                  <input
                    id="repositoryPath"
                    className="mono"
                    value={settingsDraft.repositoryPath}
                    onChange={(e) =>
                      setSettingsDraft((s) => ({ ...s, repositoryPath: e.target.value }))
                    }
                    placeholder="D:\Work\acme-website"
                  />
                </div>
                <div className="field">
                  <label htmlFor="remoteUrl">Remote URL</label>
                  <input
                    id="remoteUrl"
                    className="mono"
                    value={settingsDraft.remoteUrl}
                    onChange={(e) => setSettingsDraft((s) => ({ ...s, remoteUrl: e.target.value }))}
                    placeholder="https://github.com/acme/website.git"
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
            </form>
          </Panel>
        </section>
      )}
    </Page>
  );
}

function CostModelDetailDialog({
  modelRow,
  usageItems,
  currency,
  onClose,
}: {
  modelRow: CombinedModelCostRow;
  usageItems: ProjectUsageEntryDto[];
  currency: string;
  onClose: () => void;
}) {
  const modelKey = modelRow.model.trim().toLowerCase() || 'unknown';
  const modelUsages = usageItems.filter(
    (item) => (item.model?.trim().toLowerCase() || 'unknown') === modelKey,
  );

  const promptsById = new Map<
    string,
    {
      prompt: LinkedPromptSummaryDto;
      totalTokens: number;
      calculatedTokenCost: number;
    }
  >();
  for (const item of modelUsages) {
    const prompt = item.linkedPrompt;
    if (!prompt) continue;
    const existing = promptsById.get(prompt.id);
    if (existing) {
      existing.totalTokens += item.totalTokens;
      existing.calculatedTokenCost += item.calculatedTokenCost;
    } else {
      promptsById.set(prompt.id, {
        prompt,
        totalTokens: item.totalTokens,
        calculatedTokenCost: item.calculatedTokenCost,
      });
    }
  }
  const associatedPrompts = [...promptsById.values()].sort(
    (a, b) => b.prompt.timestampUtc.localeCompare(a.prompt.timestampUtc),
  );

  const usageTypeTotals = new Map<string, { tokens: number; calculatedCost: number }>();
  for (const item of modelUsages) {
    for (const row of item.usageByType ?? []) {
      const current = usageTypeTotals.get(row.type) ?? { tokens: 0, calculatedCost: 0 };
      current.tokens += row.tokens;
      current.calculatedCost += row.calculatedCost;
      usageTypeTotals.set(row.type, current);
    }
  }
  const usageTypeOrder = ['Input', 'Output', 'Cache read', 'Cache write', 'Reasoning'];
  const usageTypeRows: PromptUsageTypeBreakdownDto[] =
    usageTypeTotals.size > 0
      ? usageTypeOrder
          .filter((type) => usageTypeTotals.has(type))
          .map((type) => {
            const row = usageTypeTotals.get(type)!;
            return {
              type,
              tokens: row.tokens,
              calculatedCost: Math.round(row.calculatedCost * 1_000_000) / 1_000_000,
            };
          })
      : [
          { type: 'Input', tokens: modelRow.inputTokens, calculatedCost: 0 },
          { type: 'Output', tokens: modelRow.outputTokens, calculatedCost: 0 },
          { type: 'Cache read', tokens: modelRow.cachedInputTokens, calculatedCost: 0 },
          { type: 'Cache write', tokens: modelRow.cacheWriteTokens, calculatedCost: 0 },
          { type: 'Reasoning', tokens: modelRow.reasoningTokens, calculatedCost: 0 },
        ].filter((row) => row.tokens > 0 || modelRow.totalTokens === 0);

  return (
    <PopupForm
      title={`Cost detail · ${modelRow.model}`}
      subtitle={`Rate ${modelRow.rateSource} · ${formatNumber(associatedPrompts.length)} linked prompt${associatedPrompts.length === 1 ? '' : 's'}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      <div className="stack">
        <div>
          <h3>Usage by type</h3>
          {usageTypeRows.length === 0 ? (
            <EmptyState message="No token usage for this model in the selected range." />
          ) : (
            <TablePanel>
              <table className="data">
                <thead>
                  <tr>
                    <th>Usage type</th>
                    <th>Tokens</th>
                    <th>Calculated cost</th>
                  </tr>
                </thead>
                <tbody>
                  {usageTypeRows.map((row) => (
                    <tr key={row.type}>
                      <td>
                        <span className="setting-label">
                          <span>{row.type}</span>
                          <UsageTypeHelp type={row.type} />
                        </span>
                      </td>
                      <td>{formatNumber(row.tokens)}</td>
                      <td>{formatCurrency(row.calculatedCost, currency)}</td>
                    </tr>
                  ))}
                  <tr>
                    <td>
                      <strong>Total</strong>
                    </td>
                    <td>
                      <strong>
                        {formatNumber(
                          usageTypeTotals.size > 0
                            ? [...usageTypeTotals.values()].reduce((sum, row) => sum + row.tokens, 0)
                            : modelRow.totalTokens,
                        )}
                      </strong>
                    </td>
                    <td>
                      <strong>
                        {formatCurrency(
                          usageTypeTotals.size > 0
                            ? [...usageTypeTotals.values()].reduce(
                                (sum, row) => sum + row.calculatedCost,
                                0,
                              )
                            : modelRow.estimatedCost || modelRow.calculatedTokenCost,
                          currency,
                        )}
                      </strong>
                    </td>
                  </tr>
                </tbody>
              </table>
            </TablePanel>
          )}
        </div>

        <div>
          <h3>Associated prompts</h3>
          {associatedPrompts.length === 0 ? (
            <EmptyState message="No linked prompts for this model’s attributed usage in the selected range." />
          ) : (
            <TablePanel>
              <table className="data">
                <thead>
                  <tr>
                    <th>Time</th>
                    <th>Type</th>
                    <th>Model</th>
                    <th>Branch</th>
                    <th>Status</th>
                    <th>Duration</th>
                    <th>Tokens</th>
                    <th>Calculated cost</th>
                  </tr>
                </thead>
                <tbody>
                  {associatedPrompts.map(({ prompt, totalTokens, calculatedTokenCost }) => (
                    <tr key={prompt.id}>
                      <td>{formatDateTime(prompt.timestampUtc)}</td>
                      <td>{prompt.eventType}</td>
                      <td>{prompt.model ?? '—'}</td>
                      <td>{prompt.branch ?? '—'}</td>
                      <td>{prompt.status ?? '—'}</td>
                      <td>{formatDurationMs(prompt.durationMilliseconds)}</td>
                      <td>{formatNumber(totalTokens)}</td>
                      <td>{formatCurrency(calculatedTokenCost, currency)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TablePanel>
          )}
        </div>
      </div>
    </PopupForm>
  );
}

function ActivityDaySessionsDialog({
  day,
  projectId,
  onClose,
}: {
  day: string;
  projectId: string;
  onClose: () => void;
}) {
  const dayKey = day.includes('T') ? day.slice(0, 10) : day;
  const bounds = useMemo(() => dayBoundsLocal(dayKey), [dayKey]);
  const sessionsQuery = useProjectSessionsQuery(projectId, bounds.fromUtc, bounds.toUtc);
  const sessionRows = useMemo(
    () => sessionsWithinTimeRange(sessionsQuery.data ?? [], bounds.fromUtc, bounds.toUtc),
    [bounds.fromUtc, bounds.toUtc, sessionsQuery.data],
  );
  const totalMs = useMemo(
    () => sessionRows.reduce((sum, row) => sum + row.durationMs, 0),
    [sessionRows],
  );

  return (
    <PopupForm
      title={`Sessions on ${formatDay(dayKey)}`}
      subtitle="Start, end, and duration are the portions of each session on this day."
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {sessionsQuery.isLoading ? (
        <LoadingState label="Loading sessions…" />
      ) : sessionsQuery.error ? (
        <ErrorState
          message={
            sessionsQuery.error instanceof Error
              ? sessionsQuery.error.message
              : 'Failed to load sessions'
          }
        />
      ) : sessionRows.length === 0 ? (
        <EmptyState message="No sessions fall within this day." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Started</th>
                <th>Ended</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {sessionRows.map((row) => (
                <tr key={row.session.id}>
                  <td>{formatDateTime(row.startUtc)}</td>
                  <td>{formatDateTime(row.endUtc)}</td>
                  <td>
                    {formatDurationMs(row.durationMs)}
                    {row.session.isActive && !row.session.endedAtUtc ? ' (running)' : ''}
                  </td>
                </tr>
              ))}
              <tr>
                <td colSpan={2}>
                  <strong>Total</strong>
                </td>
                <td>
                  <strong>{formatDurationMs(totalMs)}</strong>
                </td>
              </tr>
            </tbody>
          </table>
        </TablePanel>
      )}
    </PopupForm>
  );
}

function ActivityDayTimesheetsDialog({
  day,
  projectId,
  onClose,
}: {
  day: string;
  projectId: string;
  onClose: () => void;
}) {
  const dayKey = day.includes('T') ? day.slice(0, 10) : day;
  const bounds = useMemo(() => dayBoundsLocal(dayKey), [dayKey]);
  const timesheetsQuery = useProjectTimesheetQuery(projectId, bounds.fromUtc, bounds.toUtc);
  const timesheetRows = useMemo(
    () => timesheetsWithinTimeRange(timesheetsQuery.data ?? [], bounds.fromUtc, bounds.toUtc),
    [bounds.fromUtc, bounds.toUtc, timesheetsQuery.data],
  );
  const totalMs = useMemo(
    () => timesheetRows.reduce((sum, row) => sum + row.durationMs, 0),
    [timesheetRows],
  );

  return (
    <PopupForm
      title={`Timesheets on ${formatDay(dayKey)}`}
      subtitle="Start, end, and duration are the portions of each timesheet entry on this day."
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {timesheetsQuery.isLoading ? (
        <LoadingState label="Loading timesheets…" />
      ) : timesheetsQuery.error ? (
        <ErrorState
          message={
            timesheetsQuery.error instanceof Error
              ? timesheetsQuery.error.message
              : 'Failed to load timesheets'
          }
        />
      ) : timesheetRows.length === 0 ? (
        <EmptyState message="No timesheet entries fall within this day." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Category</th>
                <th>Started</th>
                <th>Ended</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {timesheetRows.map((row) => (
                <tr key={row.entry.id}>
                  <td>{row.entry.categoryName?.trim() || 'Uncategorized'}</td>
                  <td>{formatDateTime(row.startUtc)}</td>
                  <td>{formatDateTime(row.endUtc)}</td>
                  <td>
                    {formatDurationMs(row.durationMs)}
                    {row.entry.isOpen && !row.entry.endedAtUtc ? ' (open)' : ''}
                  </td>
                </tr>
              ))}
              <tr>
                <td colSpan={3}>
                  <strong>Total</strong>
                </td>
                <td>
                  <strong>{formatDurationMs(totalMs)}</strong>
                </td>
              </tr>
            </tbody>
          </table>
        </TablePanel>
      )}
    </PopupForm>
  );
}

function promptEndUtc(prompt: PromptEventDto): string | null {
  if (prompt.durationMilliseconds == null || prompt.durationMilliseconds < 0) {
    return null;
  }
  const start = new Date(prompt.timestampUtc).getTime();
  if (!Number.isFinite(start)) {
    return null;
  }
  return new Date(start + prompt.durationMilliseconds).toISOString();
}

function SessionPromptsDialog({
  session,
  onClose,
}: {
  session: SessionDto;
  onClose: () => void;
}) {
  const promptsQuery = useSessionPromptsQuery(session.id);
  const prompts = useMemo(
    () =>
      [...(promptsQuery.data ?? [])].sort((a, b) =>
        b.timestampUtc.localeCompare(a.timestampUtc),
      ),
    [promptsQuery.data],
  );
  const totalDurationMs = useMemo(
    () =>
      prompts.reduce(
        (sum, prompt) =>
          sum +
          (prompt.durationMilliseconds != null && prompt.durationMilliseconds > 0
            ? prompt.durationMilliseconds
            : 0),
        0,
      ),
    [prompts],
  );
  const sessionDuration = sessionDurationMs(session);

  return (
    <PopupForm
      title="Prompts in session"
      subtitle={`${session.editor ?? 'Session'} · ${formatDateTime(session.startedAtUtc)}${
        sessionDuration == null ? '' : ` · ${formatDurationMs(sessionDuration)}`
      }`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {promptsQuery.isLoading ? (
        <LoadingState label="Loading prompts…" />
      ) : promptsQuery.error ? (
        <ErrorState
          message={
            promptsQuery.error instanceof Error
              ? promptsQuery.error.message
              : 'Failed to load prompts'
          }
        />
      ) : prompts.length === 0 ? (
        <EmptyState message="No prompts were recorded for this session." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Started</th>
                <th>Ended</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {prompts.map((prompt) => {
                const endUtc = promptEndUtc(prompt);
                return (
                  <tr key={prompt.id}>
                    <td>{formatDateTime(prompt.timestampUtc)}</td>
                    <td>{endUtc ? formatDateTime(endUtc) : '—'}</td>
                    <td>
                      {prompt.durationMilliseconds == null
                        ? '—'
                        : formatDurationMs(prompt.durationMilliseconds)}
                    </td>
                  </tr>
                );
              })}
              <tr>
                <td colSpan={2}>
                  <strong>Total</strong>
                </td>
                <td>
                  <strong>{formatDurationMs(totalDurationMs)}</strong>
                </td>
              </tr>
            </tbody>
          </table>
        </TablePanel>
      )}
    </PopupForm>
  );
}

function TimesheetSessionsDialog({
  entry,
  projectId,
  onClose,
}: {
  entry: TimesheetEntryDto;
  projectId: string;
  onClose: () => void;
}) {
  const sessionFromUtc = useMemo(() => {
    const start = new Date(entry.startedAtUtc).getTime();
    const base = Number.isFinite(start) ? start : Date.now();
    return new Date(base - 60_000).toISOString();
  }, [entry.startedAtUtc]);
  const sessionToUtc = useMemo(() => {
    const end = entry.endedAtUtc ? new Date(entry.endedAtUtc).getTime() : Date.now();
    const base = Number.isFinite(end) ? end : Date.now();
    return new Date(base + 60_000).toISOString();
  }, [entry.endedAtUtc]);

  const sessionsQuery = useProjectSessionsQuery(projectId, sessionFromUtc, sessionToUtc);
  const sessionRows = useMemo(
    () => sessionsWithinTimesheetPeriods(sessionsQuery.data ?? [], [entry]),
    [entry, sessionsQuery.data],
  );
  const totalMs = useMemo(
    () => sessionRows.reduce((sum, row) => sum + row.durationMs, 0),
    [sessionRows],
  );
  const entryDuration = timesheetEntryDurationMs(entry);

  return (
    <PopupForm
      title="Sessions in timesheet period"
      subtitle={`${entry.categoryName?.trim() || 'Uncategorized'} · ${formatDateTime(entry.startedAtUtc)} – ${
        entry.endedAtUtc ? formatDateTime(entry.endedAtUtc) : 'Open'
      }${entryDuration == null ? '' : ` · ${formatDurationMs(entryDuration)}`}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {sessionsQuery.isLoading ? (
        <LoadingState label="Loading sessions…" />
      ) : sessionsQuery.error ? (
        <ErrorState
          message={
            sessionsQuery.error instanceof Error
              ? sessionsQuery.error.message
              : 'Failed to load sessions'
          }
        />
      ) : sessionRows.length === 0 ? (
        <EmptyState message="No sessions fall within this timesheet period." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Started</th>
                <th>Ended</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {sessionRows.map((row) => (
                <tr key={row.session.id}>
                  <td>{formatDateTime(row.startUtc)}</td>
                  <td>{formatDateTime(row.endUtc)}</td>
                  <td>
                    {formatDurationMs(row.durationMs)}
                    {row.session.isActive && !row.session.endedAtUtc ? ' (running)' : ''}
                  </td>
                </tr>
              ))}
              <tr>
                <td colSpan={2}>
                  <strong>Total</strong>
                </td>
                <td>
                  <strong>{formatDurationMs(totalMs)}</strong>
                </td>
              </tr>
            </tbody>
          </table>
        </TablePanel>
      )}
    </PopupForm>
  );
}

function LinkedPromptDialog({
  usage,
  onClose,
}: {
  usage: ProjectUsageEntryDto;
  onClose: () => void;
}) {
  const prompt: LinkedPromptSummaryDto | null | undefined = usage.linkedPrompt;
  const fields: { label: string; value: string }[] = prompt
    ? [
        { label: 'Time', value: formatDateTime(prompt.timestampUtc) },
        { label: 'Type', value: prompt.eventType || '—' },
        { label: 'Editor', value: prompt.editor?.trim() || '—' },
        { label: 'Model', value: prompt.model?.trim() || '—' },
        { label: 'Branch', value: prompt.branch?.trim() || '—' },
        { label: 'Status', value: prompt.status?.trim() || '—' },
        {
          label: 'Duration',
          value:
            prompt.durationMilliseconds == null
              ? '—'
              : formatDurationMs(prompt.durationMilliseconds),
        },
        { label: 'Repository', value: prompt.repositoryPath?.trim() || '—' },
        { label: 'Workspace', value: prompt.workspacePath?.trim() || '—' },
        { label: 'Remote URL', value: prompt.remoteUrl?.trim() || '—' },
        { label: 'Attribution', value: prompt.attributionMethod?.trim() || '—' },
        { label: 'Confidence', value: prompt.attributionConfidence?.trim() || '—' },
        { label: 'Prompt id', value: prompt.id },
      ]
    : [];

  return (
    <PopupForm
      title="Linked prompt"
      subtitle={`${usage.model ?? 'Unknown model'} · ${formatDateTime(usage.timestampUtc)}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {!prompt ? (
        <EmptyState message="This usage row is not linked to a prompt (manual allocation or missing activity link)." />
      ) : (
        <div className="stack">
          <TablePanel>
            <table className="data">
              <tbody>
                {fields.map((field) => (
                  <tr key={field.label}>
                    <th scope="row">{field.label}</th>
                    <td className={field.label === 'Prompt id' ? 'mono' : undefined}>{field.value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TablePanel>
        </div>
      )}
    </PopupForm>
  );
}

function PromptUsageBreakdownDialog({
  prompt,
  onClose,
}: {
  prompt: PromptEventDto;
  onClose: () => void;
}) {
  const rows: PromptUsageTypeBreakdownDto[] = prompt.usageByType ?? [];
  const hasLinked = Boolean(prompt.hasLinkedUsage) || rows.length > 0;

  return (
    <PopupForm
      title="Prompt usage by type"
      subtitle={`${formatDateTime(prompt.timestampUtc)}${prompt.model ? ` · ${prompt.model}` : ''}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {!hasLinked ? (
        <EmptyState message="No imported usage is linked to this prompt yet. Run usage reconciliation first." />
      ) : (
        <div className="stack">
          <div className="metric-grid">
            <MetricCard
              label="Linked usages"
              value={formatNumber(prompt.linkedUsageCount ?? rows.length)}
            />
            <MetricCard label="Total tokens" value={formatNumber(prompt.totalTokens ?? 0)} />
            <MetricCard label="Reported cost" value={formatCurrency(prompt.reportedCost ?? 0)} />
            <MetricCard
              label="Calculated cost"
              value={formatCurrency(prompt.calculatedTokenCost ?? 0)}
            />
          </div>
          <TablePanel>
            <table className="data">
              <thead>
                <tr>
                  <th>Usage type</th>
                  <th>Tokens</th>
                  <th>Calculated cost</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.type}>
                    <td>
                      <span className="setting-label">
                        <span>{row.type}</span>
                        <UsageTypeHelp type={row.type} />
                      </span>
                    </td>
                    <td>{formatNumber(row.tokens)}</td>
                    <td>{formatCurrency(row.calculatedCost)}</td>
                  </tr>
                ))}
                <tr>
                  <td>
                    <strong>Total</strong>
                  </td>
                  <td>
                    <strong>{formatNumber(prompt.totalTokens ?? 0)}</strong>
                  </td>
                  <td>
                    <strong>{formatCurrency(prompt.calculatedTokenCost ?? 0)}</strong>
                  </td>
                </tr>
              </tbody>
            </table>
          </TablePanel>
          <p className="hint">
            Tokens and calculated cost come from linked Cursor usage (rate card). Reported cost is
            the import total for those linked rows.
          </p>
        </div>
      )}
    </PopupForm>
  );
}

const CURSOR_USAGE_TYPE_HELP: Record<string, string> = {
  Input:
    'Cursor Input tokens: the prompt and context sent to the model (your message, attached files, and conversation history).',
  Output: 'Cursor Output tokens: the generated reply text streamed back to you.',
  'Cache read':
    'Cursor Cache read tokens: reused cached context from the provider prompt cache (usually cheaper than Input).',
  'Cache write':
    'Cursor Cache write tokens: new context written into the provider prompt cache for later reuse.',
  Reasoning:
    'Cursor Reasoning tokens: internal “thinking” tokens some models bill separately from the visible reply.',
};

function UsageTypeHelp({ type }: { type: string }) {
  const detail = CURSOR_USAGE_TYPE_HELP[type];
  if (!detail) {
    return null;
  }

  return (
    <span
      className="setting-help"
      data-tooltip={detail}
      tabIndex={0}
      role="img"
      aria-label={detail}
      onClick={(e) => e.stopPropagation()}
      onKeyDown={(e) => e.stopPropagation()}
    >
      ?
    </span>
  );
}
