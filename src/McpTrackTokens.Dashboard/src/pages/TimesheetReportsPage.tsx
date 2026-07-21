import { useMemo, useState, type ReactNode } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
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
import { MetricCard } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import { Page } from '../layout/AppLayout';
import { type RangePreset, resolveRange } from '../utils/dateRange';
import {
  formatDateTime,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
} from '../utils/format';

const SECTIONS = ['overall', 'projects', 'clients'] as const;
type ReportSection = (typeof SECTIONS)[number];

function sectionFromPath(pathname: string): ReportSection {
  if (pathname.includes('/timesheet/reports/projects')) return 'projects';
  if (pathname.includes('/timesheet/reports/clients')) return 'clients';
  return 'overall';
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
    <div className="table-wrap">
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
    </div>
  );
}

function ProjectTable({ rows }: { rows: TimesheetProjectBreakdownRow[] }) {
  if (rows.length === 0) {
    return <EmptyState message="No project breakdown for this range." />;
  }
  return (
    <div className="table-wrap">
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
                <Link to={`/projects/${row.projectId}?tab=Timesheet`}>{row.projectName}</Link>
              </td>
              <td>{row.clientName?.trim() ? row.clientName : '—'}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ClientTable({ rows }: { rows: TimesheetClientBreakdownRow[] }) {
  if (rows.length === 0) {
    return <EmptyState message="No client breakdown for this range." />;
  }
  return (
    <div className="table-wrap">
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
                <Link to={`/timesheet/reports/clients?client=${encodeURIComponent(row.clientName)}`}>
                  {row.clientName}
                </Link>
              </td>
              <td>{formatNumber(row.projectCount)}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>{formatNumber(row.entryCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
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
    <div className="table-wrap">
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
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => onDayClick(row.day)}
                  >
                    {row.day}
                  </button>
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
    </div>
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
}: {
  fromUtc: string;
  toUtc: string;
  onDayClick: (day: string) => void;
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
  const projectChart = report.data.byProject.slice(0, 12).map((row) => ({
    name: row.projectName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));
  const clientChart = report.data.byClient.map((row) => ({
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
        <ClientTable rows={report.data.byClient} />
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
  onProjectChange,
  onDayClick,
}: {
  fromUtc: string;
  toUtc: string;
  projectId: string;
  onProjectChange: (id: string) => void;
  onDayClick: (day: string, projectIds?: string[]) => void;
}) {
  const projects = useProjectsQuery();
  const report = useTimesheetProjectReportQuery(projectId || undefined, fromUtc, toUtc, Boolean(projectId));

  const categoryChart = (report.data?.byCategory ?? []).map((row) => ({
    name: row.categoryName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));

  return (
    <div className="stack">
      <div className="panel field-row">
        <div className="field">
          <label htmlFor="timesheet-report-project">Project</label>
          <select
            id="timesheet-report-project"
            value={projectId}
            onChange={(e) => onProjectChange(e.target.value)}
          >
            <option value="">Select project…</option>
            {(projects.data ?? []).map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {!projectId ? (
        <EmptyState message="Select a project to view its timesheet report." />
      ) : report.isLoading ? (
        <LoadingState label="Loading project report…" />
      ) : report.error ? (
        <ErrorState
          message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
        />
      ) : report.data ? (
        <>
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
        </>
      ) : null}
    </div>
  );
}

function ClientReportView({
  fromUtc,
  toUtc,
  clientName,
  onClientChange,
  onDayClick,
}: {
  fromUtc: string;
  toUtc: string;
  clientName: string;
  onClientChange: (name: string) => void;
  onDayClick: (day: string, projectIds?: string[]) => void;
}) {
  const clients = useReportClientsQuery();
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

  return (
    <div className="stack">
      <div className="panel field-row">
        <div className="field">
          <label htmlFor="timesheet-report-client">Client</label>
          <select
            id="timesheet-report-client"
            value={clientName}
            onChange={(e) => onClientChange(e.target.value)}
          >
            <option value="">Select client…</option>
            {(clients.data ?? []).map((c) => (
              <option key={c.name} value={c.name}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {!clientName ? (
        <EmptyState message="Select a client to view its timesheet report." />
      ) : report.isLoading ? (
        <LoadingState label="Loading client report…" />
      ) : report.error ? (
        <ErrorState
          message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
        />
      ) : report.data ? (
        <>
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
              onDayClick={(day) =>
                onDayClick(day, report.data?.byProject.map((row) => row.projectId))
              }
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
              onDayClick={(day) =>
                onDayClick(day, report.data?.byProject.map((row) => row.projectId))
              }
            />
          </section>
        </>
      ) : null}
    </div>
  );
}

function DialogFrame({
  title,
  subtitle,
  onClose,
  children,
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: ReactNode;
}) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <div
        className="modal-panel"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="section-header">
          <div>
            <h2>{title}</h2>
            {subtitle ? <p className="muted">{subtitle}</p> : null}
          </div>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
        </div>
        {children}
      </div>
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
  const allowedProjectIds = useMemo(() => new Set(projectIds ?? []), [projectIds]);
  const visibleSessions = (sessions.data ?? []).filter(
    (session) =>
      allowedProjectIds.size === 0 ||
      (session.projectId ? allowedProjectIds.has(session.projectId) : false),
  );

  return (
    <DialogFrame
      title={`Sessions on ${day}`}
      subtitle="Click a session to view its prompts."
      onClose={onClose}
    >
      {sessions.isLoading ? (
        <LoadingState label="Loading sessions…" />
      ) : sessions.error ? (
        <ErrorState
          message={sessions.error instanceof Error ? sessions.error.message : 'Failed to load sessions'}
        />
      ) : visibleSessions.length === 0 ? (
        <EmptyState message="No sessions were active on this day for this report." />
      ) : (
        <div className="table-wrap">
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
        </div>
      )}
    </DialogFrame>
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

  return (
    <DialogFrame
      title="Session prompts"
      subtitle={`${projectName ?? session.projectId ?? 'Unknown project'} · ${formatDateTime(session.startedAtUtc)}`}
      onClose={onClose}
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
        <div className="table-wrap">
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
              {prompts.data.map((prompt: PromptEventDto) => (
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
        </div>
      )}
    </DialogFrame>
  );
}

export function TimesheetReportsPage() {
  const { pathname } = useLocation();
  const section = sectionFromPath(pathname);
  const [rangePreset, setRangePreset] = useState<RangePreset>('30d');
  const range = useMemo(() => resolveRange(rangePreset), [rangePreset]);
  const projects = useProjectsQuery();

  const [projectId, setProjectId] = useState('');
  const [clientName, setClientName] = useState(() => {
    const params = new URLSearchParams(window.location.search);
    return params.get('client') ?? '';
  });
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

  const sectionLinks: { to: string; label: string; end?: boolean }[] = [
    { to: '/timesheet/reports/overall', label: 'Overall', end: true },
    { to: '/timesheet/reports/projects', label: 'By project', end: true },
    { to: '/timesheet/reports/clients', label: 'By client', end: true },
  ];

  const openDay = (day: string, projectIds?: string[]) => {
    setSelectedSession(null);
    setSelectedDay({ day, projectIds: projectIds?.filter(Boolean) });
  };

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Timesheet reports</h2>
            <p className="muted">
              Billable time rolled up by category, project, client, and day. Open entries count
              through now within the selected range.
            </p>
          </div>
          <Link to="/timesheet" className="btn btn-secondary">
            Back to entries
          </Link>
        </div>

        <div className="tabs" role="tablist" aria-label="Timesheet report views">
          {sectionLinks.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={({ isActive }) => `tab${isActive ? ' active' : ''}`}
              role="tab"
            >
              {link.label}
            </NavLink>
          ))}
        </div>

        <div className="panel field-row">
          <div className="field">
            <label htmlFor="timesheet-report-range">Range</label>
            <select
              id="timesheet-report-range"
              value={rangePreset}
              onChange={(e) => setRangePreset(e.target.value as RangePreset)}
            >
              <option value="7d">Last 7 days</option>
              <option value="30d">Last 30 days</option>
              <option value="90d">Last 90 days</option>
              <option value="month">This month</option>
            </select>
          </div>
          <div className="field">
            <label className="label">Period</label>
            <p className="hint">{range.label}</p>
          </div>
        </div>

        {section === 'overall' ? (
          <OverallReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            onDayClick={openDay}
          />
        ) : section === 'projects' ? (
          <ProjectReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            projectId={projectId}
            onProjectChange={setProjectId}
            onDayClick={openDay}
          />
        ) : (
          <ClientReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            clientName={clientName}
            onClientChange={setClientName}
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
    </Page>
  );
}
