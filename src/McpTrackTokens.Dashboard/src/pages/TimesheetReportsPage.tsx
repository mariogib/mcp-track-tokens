import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  useProjectsQuery,
  useReportClientsQuery,
  useSessionPromptsQuery,
  useSessionsQuery,
  useTimesheetClientReportQuery,
  useTimesheetOverallReportQuery,
  useTimesheetProjectReportQuery,
} from '../api/hooks';
import type {
  PromptEventDto,
  SessionDto,
  TimesheetCategoryBreakdownRow,
  TimesheetClientBreakdownRow,
  TimesheetDailyBreakdownRow,
  TimesheetProjectBreakdownRow,
  TimesheetReportTotals,
} from '../api/types';
import { ChartCard, DailyLineChart, NamedBarChart, NamedPieChart } from '../components/Charts';
import { MetricCard, Panel, TablePanel } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import { PopupForm, TextLink } from '../shared/adminUi';
import { parseRangePreset, resolveRange } from '../utils/dateRange';
import {
  formatDateTime,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
} from '../utils/format';

type ReportScope = 'all' | 'project' | 'client';

function parseReportScope(value: string | null): ReportScope {
  if (value === 'project' || value === 'client') return value;
  return 'all';
}

function hoursFromSeconds(seconds: number): number {
  return Math.round((seconds / 3600) * 100) / 100;
}

function dayBoundsUtc(day: string): { fromUtc: string; toUtc: string } {
  return {
    fromUtc: `${day}T00:00:00.000Z`,
    toUtc: `${day}T23:59:59.999Z`,
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

function TotalsCards({ totals }: { totals: TimesheetReportTotals }) {
  return (
    <div className="metric-grid">
      <MetricCard
        label="Total time"
        value={formatDurationSeconds(totals.totalDurationSeconds)}
      />
      <MetricCard label="Entries" value={formatNumber(totals.entryCount)} />
      <MetricCard label="Open entries" value={formatNumber(totals.openEntryCount)} />
    </div>
  );
}

function CategoryTable({ rows }: { rows: TimesheetCategoryBreakdownRow[] }) {
  if (rows.length === 0) {
    return <EmptyState message="No category breakdown for this range." />;
  }
  return (
    <TablePanel>
      <table className="data">
        <thead>
          <tr>
            <th>Category</th>
            <th>Duration</th>
            <th>Entries</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.categoryId}>
              <td>{row.categoryName}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TablePanel>
  );
}

function ProjectTable({ rows }: { rows: TimesheetProjectBreakdownRow[] }) {
  if (rows.length === 0) {
    return <EmptyState message="No project breakdown for this range." />;
  }
  return (
    <TablePanel>
      <table className="data">
        <thead>
          <tr>
            <th>Project</th>
            <th>Client</th>
            <th>Duration</th>
            <th>Entries</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.projectId}>
              <td>
                <TextLink to={`/projects/${row.projectId}?tab=Timesheet`}>{row.projectName}</TextLink>
              </td>
              <td>{row.clientName?.trim() ? row.clientName : '—'}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TablePanel>
  );
}

function ClientTable({
  rows,
  onClientClick,
}: {
  rows: TimesheetClientBreakdownRow[];
  onClientClick?: (clientName: string) => void;
}) {
  if (rows.length === 0) {
    return <EmptyState message="No client breakdown for this range." />;
  }
  return (
    <TablePanel>
      <table className="data">
        <thead>
          <tr>
            <th>Client</th>
            <th>Projects</th>
            <th>Duration</th>
            <th>Entries</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.clientName}>
              <td>
                {onClientClick ? (
                  <TextLink onClick={() => onClientClick(row.clientName)}>{row.clientName}</TextLink>
                ) : (
                  row.clientName
                )}
              </td>
              <td>{formatNumber(row.projectCount)}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TablePanel>
  );
}

function DailyTable({
  rows,
  onDayClick,
}: {
  rows: TimesheetDailyBreakdownRow[];
  onDayClick?: (day: string) => void;
}) {
  if (rows.length === 0) {
    return <EmptyState message="No daily activity for this range." />;
  }
  return (
    <TablePanel>
      <table className="data">
        <thead>
          <tr>
            <th>Day (UTC)</th>
            <th>Duration</th>
            <th>Entries</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.day}>
              <td>
                {onDayClick ? (
                  <TextLink onClick={() => onDayClick(row.day)}>{row.day}</TextLink>
                ) : (
                  row.day
                )}
              </td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TablePanel>
  );
}

function DailyChart({
  rows,
  onDayClick,
}: {
  rows: TimesheetDailyBreakdownRow[];
  onDayClick?: (day: string) => void;
}) {
  const data = [...rows]
    .sort((a, b) => a.day.localeCompare(b.day))
    .map((row) => ({
      day: row.day,
      hours: hoursFromSeconds(row.durationSeconds),
    }));
  if (data.length === 0) {
    return null;
  }
  return (
    <ChartCard title="Daily hours">
      <DailyLineChart
        data={data}
        xKey="day"
        yKey="hours"
        yLabel="Hours"
        onPointClick={onDayClick ? (point) => onDayClick(String(point.day)) : undefined}
      />
    </ChartCard>
  );
}

function OverallReportView({
  fromUtc,
  toUtc,
  onDayClick,
  onClientClick,
}: {
  fromUtc: string;
  toUtc: string;
  onDayClick: (day: string) => void;
  onClientClick: (clientName: string) => void;
}) {
  const report = useTimesheetOverallReportQuery(fromUtc, toUtc);
  if (report.isLoading) return <LoadingState label="Loading overall report…" />;
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (!report.data) return null;

  const categoryChart = report.data.byCategory.map((row) => ({
    name: row.categoryName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));
  const projectChart = report.data.byProject
    .filter((row) => row.durationSeconds > 0)
    .slice(0, 12)
    .map((row) => ({
      name: row.projectName,
      hours: hoursFromSeconds(row.durationSeconds),
    }));
  const clientChart = report.data.byClient
    .filter((row) => row.durationSeconds > 0)
    .map((row) => ({
      name: row.clientName,
      hours: hoursFromSeconds(row.durationSeconds),
    }));

  return (
    <div className="stack">
      <TotalsCards totals={report.data.totals} />
      <div className="chart-grid">
        {categoryChart.length > 0 ? (
          <ChartCard title="By category (hours)">
            <NamedPieChart data={categoryChart} nameKey="name" valueKey="hours" />
          </ChartCard>
        ) : null}
        {projectChart.length > 0 ? (
          <ChartCard title="Top projects (hours)">
            <NamedBarChart data={projectChart} valueKey="hours" valueLabel="Hours" />
          </ChartCard>
        ) : null}
        {clientChart.length > 0 ? (
          <ChartCard title="By client (hours)">
            <NamedBarChart data={clientChart} valueKey="hours" valueLabel="Hours" />
          </ChartCard>
        ) : null}
        <DailyChart rows={report.data.byDay} onDayClick={onDayClick} />
      </div>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable rows={report.data.byCategory} />
      </section>
      <section className="page-section">
        <h3>By project</h3>
        <ProjectTable rows={report.data.byProject} />
      </section>
      <section className="page-section">
        <h3>By client</h3>
        <ClientTable rows={report.data.byClient} onClientClick={onClientClick} />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable rows={report.data.byDay} onDayClick={onDayClick} />
      </section>
    </div>
  );
}

function ProjectReportView({
  fromUtc,
  toUtc,
  projectId,
  onDayClick,
}: {
  fromUtc: string;
  toUtc: string;
  projectId: string;
  onDayClick: (day: string, projectIds?: string[]) => void;
}) {
  const report = useTimesheetProjectReportQuery(projectId || undefined, fromUtc, toUtc, Boolean(projectId));

  const categoryChart = (report.data?.byCategory ?? []).map((row) => ({
    name: row.categoryName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));

  if (!projectId) {
    return <EmptyState message="Select a project to view its timesheet report." />;
  }
  if (report.isLoading) return <LoadingState label="Loading project report…" />;
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (!report.data) return null;

  return (
    <div className="stack">
      <p className="muted">
        {report.data.projectName}
        {report.data.clientName?.trim() ? ` · ${report.data.clientName}` : ''}
      </p>
      <TotalsCards totals={report.data.totals} />
      <div className="chart-grid">
        {categoryChart.length > 0 ? (
          <ChartCard title="By category (hours)">
            <NamedPieChart data={categoryChart} nameKey="name" valueKey="hours" />
          </ChartCard>
        ) : null}
        <DailyChart rows={report.data.byDay} onDayClick={(day) => onDayClick(day, [projectId])} />
      </div>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable rows={report.data.byCategory} />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable rows={report.data.byDay} onDayClick={(day) => onDayClick(day, [projectId])} />
      </section>
    </div>
  );
}

function ClientReportView({
  fromUtc,
  toUtc,
  clientName,
  onDayClick,
}: {
  fromUtc: string;
  toUtc: string;
  clientName: string;
  onDayClick: (day: string, projectIds?: string[]) => void;
}) {
  const report = useTimesheetClientReportQuery(
    clientName || undefined,
    fromUtc,
    toUtc,
    Boolean(clientName),
  );

  const categoryChart = (report.data?.byCategory ?? []).map((row) => ({
    name: row.categoryName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));
  const projectChart = (report.data?.byProject ?? []).map((row) => ({
    name: row.projectName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));

  if (!clientName) {
    return <EmptyState message="Select a client to view its timesheet report." />;
  }
  if (report.isLoading) return <LoadingState label="Loading client report…" />;
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (!report.data) return null;

  return (
    <div className="stack">
      <p className="muted">{report.data.clientName}</p>
      <TotalsCards totals={report.data.totals} />
      <div className="chart-grid">
        {projectChart.length > 0 ? (
          <ChartCard title="By project (hours)">
            <NamedBarChart data={projectChart} valueKey="hours" valueLabel="Hours" />
          </ChartCard>
        ) : null}
        {categoryChart.length > 0 ? (
          <ChartCard title="By category (hours)">
            <NamedPieChart data={categoryChart} nameKey="name" valueKey="hours" />
          </ChartCard>
        ) : null}
        <DailyChart
          rows={report.data.byDay}
          onDayClick={(day) => onDayClick(day, report.data?.byProject.map((row) => row.projectId))}
        />
      </div>
      <section className="page-section">
        <h3>By project</h3>
        <ProjectTable rows={report.data.byProject} />
      </section>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable rows={report.data.byCategory} />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable
          rows={report.data.byDay}
          onDayClick={(day) => onDayClick(day, report.data?.byProject.map((row) => row.projectId))}
        />
      </section>
    </div>
  );
}

function DaySessionsDialog({
  day,
  projectIds,
  projectNameById,
  onClose,
  onSessionClick,
}: {
  day: string;
  projectIds?: string[];
  projectNameById: Map<string, string>;
  onClose: () => void;
  onSessionClick: (session: SessionDto) => void;
}) {
  const bounds = useMemo(() => dayBoundsUtc(day), [day]);
  const singleProjectId = projectIds?.length === 1 ? projectIds[0] : undefined;
  const sessions = useSessionsQuery({
    projectId: singleProjectId,
    fromUtc: bounds.fromUtc,
    toUtc: bounds.toUtc,
  });
  const [statusFilter, setStatusFilter] = useState('');
  const allowedProjectIds = useMemo(() => new Set(projectIds ?? []), [projectIds]);
  const visibleSessions = useMemo(() => {
    return (sessions.data ?? []).filter((session) => {
      const matchesProject =
        allowedProjectIds.size === 0 ||
        (session.projectId ? allowedProjectIds.has(session.projectId) : false);
      if (!matchesProject) {
        return false;
      }
      if (!statusFilter) {
        return true;
      }
      const status = session.status ?? (session.isActive ? 'Active' : '—');
      return status === statusFilter;
    });
  }, [allowedProjectIds, sessions.data, statusFilter]);

  const sessionStatusOptions = useMemo(() => {
    const values = new Set<string>();
    for (const session of sessions.data ?? []) {
      if (
        allowedProjectIds.size > 0 &&
        !(session.projectId && allowedProjectIds.has(session.projectId))
      ) {
        continue;
      }
      values.add(session.status ?? (session.isActive ? 'Active' : '—'));
    }
    return [...values].sort((a, b) => a.localeCompare(b));
  }, [allowedProjectIds, sessions.data]);

  return (
    <PopupForm
      title={`Sessions on ${day}`}
      subtitle="Click a session to view its prompts."
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {sessions.isLoading ? (
        <LoadingState label="Loading sessions…" />
      ) : sessions.error ? (
        <ErrorState
          message={sessions.error instanceof Error ? sessions.error.message : 'Failed to load sessions'}
        />
      ) : (sessions.data ?? []).length === 0 ||
        (sessions.data ?? []).every(
          (session) =>
            allowedProjectIds.size > 0 &&
            !(session.projectId && allowedProjectIds.has(session.projectId)),
        ) ? (
        <EmptyState message="No sessions were active on this day for this report." />
      ) : (
        <div className="stack">
          <div className="field" style={{ maxWidth: '14rem' }}>
            <label htmlFor="report-session-status-filter">Status</label>
            <select
              id="report-session-status-filter"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">All statuses</option>
              {sessionStatusOptions.map((status) => (
                <option key={status} value={status}>
                  {status}
                </option>
              ))}
            </select>
          </div>
          {visibleSessions.length === 0 ? (
            <EmptyState message="No sessions match the current status filter." />
          ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Started</th>
                <th>Ended</th>
                <th>Project</th>
                <th>Editor</th>
                <th>Branch</th>
                <th>Duration</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {visibleSessions.map((session) => {
                const durationMs = sessionDurationMs(session);
                return (
                  <tr
                    key={session.id}
                    className="clickable-row"
                    onClick={() => onSessionClick(session)}
                  >
                    <td>{formatDateTime(session.startedAtUtc)}</td>
                    <td>{formatDateTime(session.endedAtUtc)}</td>
                    <td>
                      {session.projectId
                        ? projectNameById.get(session.projectId) ?? session.projectId
                        : '—'}
                    </td>
                    <td>{session.editor ?? '—'}</td>
                    <td>{session.branch?.trim() ? session.branch : '—'}</td>
                    <td>{durationMs == null ? '—' : formatDurationMs(durationMs)}</td>
                    <td>{session.status ?? (session.isActive ? 'Active' : '—')}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </TablePanel>
          )}
        </div>
      )}
    </PopupForm>
  );
}

function SessionPromptsDialog({
  session,
  projectName,
  onClose,
}: {
  session: SessionDto;
  projectName?: string;
  onClose: () => void;
}) {
  const prompts = useSessionPromptsQuery(session.id);
  const [statusFilter, setStatusFilter] = useState('');

  const statusOptions = useMemo(() => {
    const values = new Set<string>();
    for (const prompt of prompts.data ?? []) {
      values.add(prompt.status?.trim() || '—');
    }
    return [...values].sort((a, b) => a.localeCompare(b));
  }, [prompts.data]);

  const filteredPrompts = useMemo(() => {
    const list = prompts.data ?? [];
    if (!statusFilter) {
      return list;
    }
    return list.filter((prompt) => (prompt.status?.trim() || '—') === statusFilter);
  }, [prompts.data, statusFilter]);

  return (
    <PopupForm
      title="Session prompts"
      subtitle={`${projectName ?? session.projectId ?? 'Unknown project'} · ${formatDateTime(session.startedAtUtc)}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {prompts.isLoading ? (
        <LoadingState label="Loading prompts…" />
      ) : prompts.error ? (
        <ErrorState
          message={prompts.error instanceof Error ? prompts.error.message : 'Failed to load prompts'}
        />
      ) : !prompts.data || prompts.data.length === 0 ? (
        <EmptyState message="No prompt submissions were recorded for this session." />
      ) : (
        <div className="stack">
          <div className="field" style={{ maxWidth: '14rem' }}>
            <label htmlFor="report-prompt-status-filter">Status</label>
            <select
              id="report-prompt-status-filter"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">All statuses</option>
              {statusOptions.map((status) => (
                <option key={status} value={status}>
                  {status}
                </option>
              ))}
            </select>
          </div>
          {filteredPrompts.length === 0 ? (
            <EmptyState message="No prompts match the current status filter." />
          ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>When</th>
                <th>Model</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Tokens</th>
                <th>Repository</th>
              </tr>
            </thead>
            <tbody>
              {filteredPrompts.map((prompt: PromptEventDto) => (
                <tr key={prompt.id}>
                  <td>{formatDateTime(prompt.timestampUtc)}</td>
                  <td>{prompt.model?.trim() ? prompt.model : '—'}</td>
                  <td>{prompt.status ?? '—'}</td>
                  <td>
                    {prompt.durationMilliseconds == null
                      ? '—'
                      : formatDurationMs(prompt.durationMilliseconds)}
                  </td>
                  <td>{prompt.totalTokens == null ? '—' : formatNumber(prompt.totalTokens)}</td>
                  <td>{prompt.repositoryPath?.trim() ? prompt.repositoryPath : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TablePanel>
          )}
        </div>
      )}
    </PopupForm>
  );
}

export function TimesheetReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const scope = parseReportScope(searchParams.get('scope'));
  const rangePreset = parseRangePreset(searchParams.get('range'));
  const projectId = searchParams.get('project') ?? '';
  const clientName = searchParams.get('client') ?? '';
  const range = useMemo(() => resolveRange(rangePreset), [rangePreset]);

  const projects = useProjectsQuery();
  const clients = useReportClientsQuery();

  const [selectedDay, setSelectedDay] = useState<{
    day: string;
    projectIds?: string[];
  } | null>(null);
  const [selectedSession, setSelectedSession] = useState<SessionDto | null>(null);

  const projectNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const project of projects.data ?? []) {
      map.set(project.id, project.name);
    }
    return map;
  }, [projects.data]);

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

  const setScope = (next: ReportScope) => {
    updateParams({
      scope: next === 'all' ? null : next,
      project: next === 'project' ? projectId || null : null,
      client: next === 'client' ? clientName || null : null,
    });
  };

  const openDay = (day: string, projectIds?: string[]) => {
    setSelectedSession(null);
    setSelectedDay({ day, projectIds: projectIds?.filter(Boolean) });
  };

  const focusClient = (name: string) => {
    updateParams({
      scope: 'client',
      client: name,
      project: null,
    });
  };

  return (
    <>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Reports</h2>
            <p className="muted">
              Billable time by range, optionally filtered to one project or client. Open entries
              count through now within the selected range.
            </p>
          </div>
        </div>

        <Panel className="field-row">
          <div className="field">
            <label htmlFor="timesheet-report-scope">Scope</label>
            <select
              id="timesheet-report-scope"
              value={scope}
              onChange={(e) => setScope(e.target.value as ReportScope)}
            >
              <option value="all">All projects</option>
              <option value="project">One project</option>
              <option value="client">One client</option>
            </select>
          </div>
          <div className="field">
            <label htmlFor="timesheet-report-range">Range</label>
            <select
              id="timesheet-report-range"
              value={rangePreset === 'custom' ? '30d' : rangePreset}
              onChange={(e) => updateParams({ range: e.target.value })}
            >
              <option value="7d">Last 7 days</option>
              <option value="30d">Last 30 days</option>
              <option value="90d">Last 90 days</option>
              <option value="month">This month</option>
            </select>
          </div>
          {scope === 'project' ? (
            <div className="field">
              <label htmlFor="timesheet-report-project">Project</label>
              <select
                id="timesheet-report-project"
                value={projectId}
                onChange={(e) => updateParams({ project: e.target.value || null })}
              >
                <option value="">Select project…</option>
                {(projects.data ?? []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
          ) : null}
          {scope === 'client' ? (
            <div className="field">
              <label htmlFor="timesheet-report-client">Client</label>
              <select
                id="timesheet-report-client"
                value={clientName}
                onChange={(e) => updateParams({ client: e.target.value || null })}
              >
                <option value="">Select client…</option>
                {(clients.data ?? []).map((c) => (
                  <option key={c.name} value={c.name}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>
          ) : null}
          <div className="field">
            <label className="label">Period</label>
            <p className="hint">{range.label}</p>
          </div>
        </Panel>

        {scope === 'all' ? (
          <OverallReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            onDayClick={openDay}
            onClientClick={focusClient}
          />
        ) : scope === 'project' ? (
          <ProjectReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            projectId={projectId}
            onDayClick={openDay}
          />
        ) : (
          <ClientReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            clientName={clientName}
            onDayClick={openDay}
          />
        )}
      </section>
      {selectedDay ? (
        <DaySessionsDialog
          day={selectedDay.day}
          projectIds={selectedDay.projectIds}
          projectNameById={projectNameById}
          onClose={() => {
            setSelectedDay(null);
            setSelectedSession(null);
          }}
          onSessionClick={setSelectedSession}
        />
      ) : null}
      {selectedSession ? (
        <SessionPromptsDialog
          session={selectedSession}
          projectName={
            selectedSession.projectId ? projectNameById.get(selectedSession.projectId) : undefined
          }
          onClose={() => setSelectedSession(null)}
        />
      ) : null}
    </>
  );
}
