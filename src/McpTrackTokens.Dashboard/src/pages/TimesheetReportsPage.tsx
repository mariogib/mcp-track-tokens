import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  useProjectsQuery,
  useReportClientsQuery,
  useSessionPromptsQuery,
  useSessionsQuery,
  useTimesheetClientReportQuery,
  useTimesheetEntriesQuery,
  useTimesheetOverallReportQuery,
  useTimesheetProjectReportQuery,
  useTimesheetReportMonthsQuery,
} from '../api/hooks';
import type {
  PromptEventDto,
  SessionDto,
  TimesheetCategoryBreakdownRow,
  TimesheetClientBreakdownRow,
  TimesheetDailyBreakdownRow,
  TimesheetEntryDto,
  TimesheetProjectBreakdownRow,
  TimesheetReportTotals,
} from '../api/types';
import { ChartCard, DailyLineChart, NamedBarChart, NamedPieChart } from '../components/Charts';
import { DateRangeFilters } from '../components/DateRangeFilters';
import { MetricCard, Panel, TablePanel } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { PopupForm, TextLink } from '../shared/adminUi';
import { sessionDurationMs, timesheetEntryDurationMs } from '../utils/duration';
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
  formatDateTime,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
} from '../utils/format';

type ReportScope = 'all' | 'project' | 'client';

type TimesheetEntriesDrilldown = {
  title: string;
  fromUtc: string;
  toUtc: string;
  projectIds?: string[];
  categoryId?: string;
  openOnly?: boolean;
  /** When set, only entries that started on this local calendar day are shown. */
  day?: string;
};

function parseReportScope(value: string | null): ReportScope {
  if (value === 'project' || value === 'client') return value;
  return 'all';
}

function hoursFromSeconds(seconds: number): number {
  return Math.round((seconds / 3600) * 100) / 100;
}

/** Local-calendar day bounds as UTC instants (matches project timesheet day grouping). */
function dayBoundsLocal(day: string): { fromUtc: string; toUtc: string } {
  const [year, month, dayOfMonth] = day.split('-').map(Number);
  const from = new Date(year, month - 1, dayOfMonth, 0, 0, 0, 0);
  const to = new Date(year, month - 1, dayOfMonth, 23, 59, 59, 999);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

function toLocalDayKey(iso: string): string | null {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function shiftLocalDayKey(dayKey: string, deltaDays: number): string {
  const [year, month, day] = dayKey.split('-').map(Number);
  const date = new Date(year, month - 1, day);
  date.setDate(date.getDate() + deltaDays);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function sessionOverlapsLocalDay(session: SessionDto, day: string): boolean {
  const bounds = dayBoundsLocal(day);
  const start = new Date(session.startedAtUtc).getTime();
  const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : Date.now();
  const from = new Date(bounds.fromUtc).getTime();
  const to = new Date(bounds.toUtc).getTime();
  if (
    Number.isNaN(start) ||
    Number.isNaN(end) ||
    Number.isNaN(from) ||
    Number.isNaN(to) ||
    end < start
  ) {
    return false;
  }
  return start <= to && end >= from;
}

/** Build by-day rows from entries using local start day — same rules as the entries popup. */
function buildLocalDailyBreakdown(
  entries: TimesheetEntryDto[],
  fromUtc: string,
  toUtc: string,
  sessions: SessionDto[] = [],
): TimesheetDailyBreakdownRow[] {
  const fromDay = toLocalDayKey(fromUtc);
  const toDay = toLocalDayKey(toUtc);
  if (!fromDay || !toDay) return [];

  const buckets = new Map<
    string,
    { durationSeconds: number; entryIds: Set<string>; sessionIds: Set<string> }
  >();
  for (let day = fromDay; day <= toDay; day = shiftLocalDayKey(day, 1)) {
    buckets.set(day, { durationSeconds: 0, entryIds: new Set(), sessionIds: new Set() });
    if (day === toDay) break;
  }

  for (const entry of entries) {
    const day = toLocalDayKey(entry.startedAtUtc);
    if (!day || day < fromDay || day > toDay) continue;
    const bucket = buckets.get(day);
    if (!bucket) continue;
    bucket.entryIds.add(entry.id);
    const durationMs = timesheetEntryDurationMs(entry);
    if (durationMs != null && durationMs > 0) {
      bucket.durationSeconds += Math.floor(durationMs / 1000);
    }
  }

  for (const session of sessions) {
    for (let day = fromDay; day <= toDay; day = shiftLocalDayKey(day, 1)) {
      const bucket = buckets.get(day);
      if (bucket && sessionOverlapsLocalDay(session, day)) {
        bucket.sessionIds.add(session.id);
      }
      if (day === toDay) break;
    }
  }

  return [...buckets.entries()]
    .map(([day, bucket]) => ({
      day,
      durationSeconds: bucket.durationSeconds,
      entryCount: bucket.entryIds.size,
      sessionCount: bucket.sessionIds.size,
    }))
    .sort((a, b) => b.day.localeCompare(a.day));
}

function EntryCountLink({
  count,
  onClick,
}: {
  count: number;
  onClick?: () => void;
}) {
  const label = formatNumber(count);
  if (!onClick || count <= 0) {
    return <>{label}</>;
  }
  return <TextLink onClick={onClick}>{label}</TextLink>;
}

function TotalsCards({
  totals,
  onEntriesClick,
  onOpenEntriesClick,
}: {
  totals: TimesheetReportTotals;
  onEntriesClick?: () => void;
  onOpenEntriesClick?: () => void;
}) {
  return (
    <div className="metric-grid">
      <MetricCard
        label="Total time"
        value={formatDurationSeconds(totals.totalDurationSeconds)}
      />
      <MetricCard
        label="Timesheet entries"
        value={formatNumber(totals.entryCount)}
        onClick={totals.entryCount > 0 ? onEntriesClick : undefined}
      />
      <MetricCard
        label="Open timesheet entries"
        value={formatNumber(totals.openEntryCount)}
        onClick={totals.openEntryCount > 0 ? onOpenEntriesClick : undefined}
      />
    </div>
  );
}

function CategoryTable({
  rows,
  onEntriesClick,
}: {
  rows: TimesheetCategoryBreakdownRow[];
  onEntriesClick?: (row: TimesheetCategoryBreakdownRow) => void;
}) {
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
            <th>Timesheet entries</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.categoryId}>
              <td>{row.categoryName}</td>
              <td>{formatDurationSeconds(row.durationSeconds)}</td>
              <td>
                <EntryCountLink
                  count={row.entryCount}
                  onClick={onEntriesClick ? () => onEntriesClick(row) : undefined}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TablePanel>
  );
}

function ProjectTable({
  rows,
  onEntriesClick,
}: {
  rows: TimesheetProjectBreakdownRow[];
  onEntriesClick?: (row: TimesheetProjectBreakdownRow) => void;
}) {
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
            <th>Timesheet entries</th>
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
              <td>
                <EntryCountLink
                  count={row.entryCount}
                  onClick={onEntriesClick ? () => onEntriesClick(row) : undefined}
                />
              </td>
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
  onEntriesClick,
}: {
  rows: TimesheetClientBreakdownRow[];
  onClientClick?: (clientName: string) => void;
  onEntriesClick?: (row: TimesheetClientBreakdownRow) => void;
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
            <th>Timesheet entries</th>
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
              <td>
                <EntryCountLink
                  count={row.entryCount}
                  onClick={onEntriesClick ? () => onEntriesClick(row) : undefined}
                />
              </td>
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
  onEntriesClick,
}: {
  rows: TimesheetDailyBreakdownRow[];
  onDayClick?: (day: string) => void;
  onEntriesClick?: (day: string) => void;
}) {
  if (rows.length === 0) {
    return <EmptyState message="No daily activity for this range." />;
  }
  return (
    <TablePanel>
      <table className="data">
        <thead>
          <tr>
            <th>Day</th>
            <th>Duration</th>
            <th>Timesheet entries</th>
            <th>Sessions</th>
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
              <td>
                <EntryCountLink
                  count={row.entryCount}
                  onClick={onEntriesClick ? () => onEntriesClick(row.day) : undefined}
                />
              </td>
              <td>
                <EntryCountLink
                  count={row.sessionCount}
                  onClick={onDayClick ? () => onDayClick(row.day) : undefined}
                />
              </td>
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
  onEntriesClick,
}: {
  fromUtc: string;
  toUtc: string;
  onDayClick: (day: string) => void;
  onClientClick: (clientName: string) => void;
  onEntriesClick: (drilldown: TimesheetEntriesDrilldown) => void;
}) {
  const report = useTimesheetOverallReportQuery(fromUtc, toUtc);
  const entries = useTimesheetEntriesQuery({ fromUtc, toUtc });
  const sessions = useSessionsQuery({ fromUtc, toUtc });
  const byDay = useMemo(
    () => buildLocalDailyBreakdown(entries.data ?? [], fromUtc, toUtc, sessions.data ?? []),
    [entries.data, fromUtc, sessions.data, toUtc],
  );

  if (report.isLoading || entries.isLoading || sessions.isLoading) {
    return <LoadingState label="Loading overall report…" />;
  }
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (entries.error) {
    return (
      <ErrorState
        message={
          entries.error instanceof Error ? entries.error.message : 'Failed to load timesheet entries'
        }
      />
    );
  }
  if (sessions.error) {
    return (
      <ErrorState
        message={
          sessions.error instanceof Error ? sessions.error.message : 'Failed to load sessions'
        }
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

  const openRange = (patch: Partial<TimesheetEntriesDrilldown> & Pick<TimesheetEntriesDrilldown, 'title'>) =>
    onEntriesClick({ fromUtc, toUtc, ...patch });

  return (
    <div className="stack">
      <TotalsCards
        totals={report.data.totals}
        onEntriesClick={() => openRange({ title: 'Timesheet entries' })}
        onOpenEntriesClick={() =>
          openRange({ title: 'Open timesheet entries', openOnly: true })
        }
      />
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
        <DailyChart rows={byDay} onDayClick={onDayClick} />
      </div>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable
          rows={report.data.byCategory}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.categoryName}`,
              categoryId: row.categoryId,
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By project</h3>
        <ProjectTable
          rows={report.data.byProject}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.projectName}`,
              projectIds: [row.projectId],
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By client</h3>
        <ClientTable
          rows={report.data.byClient}
          onClientClick={onClientClick}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.clientName}`,
              projectIds: report.data.byProject
                .filter((project) => (project.clientName?.trim() || '') === row.clientName)
                .map((project) => project.projectId),
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable
          rows={byDay}
          onDayClick={onDayClick}
          onEntriesClick={(day) => {
            const bounds = dayBoundsLocal(day);
            openRange({
              title: `Timesheet entries on ${day}`,
              fromUtc: bounds.fromUtc,
              toUtc: bounds.toUtc,
              day,
            });
          }}
        />
      </section>
    </div>
  );
}

function ProjectReportView({
  fromUtc,
  toUtc,
  projectId,
  onDayClick,
  onEntriesClick,
}: {
  fromUtc: string;
  toUtc: string;
  projectId: string;
  onDayClick: (day: string, projectIds?: string[]) => void;
  onEntriesClick: (drilldown: TimesheetEntriesDrilldown) => void;
}) {
  const report = useTimesheetProjectReportQuery(projectId || undefined, fromUtc, toUtc, Boolean(projectId));
  const entries = useTimesheetEntriesQuery(
    { projectId: projectId || undefined, fromUtc, toUtc },
    Boolean(projectId),
  );
  const sessions = useSessionsQuery(
    { projectId: projectId || undefined, fromUtc, toUtc },
    Boolean(projectId),
  );
  const byDay = useMemo(
    () => buildLocalDailyBreakdown(entries.data ?? [], fromUtc, toUtc, sessions.data ?? []),
    [entries.data, fromUtc, sessions.data, toUtc],
  );

  const categoryChart = (report.data?.byCategory ?? []).map((row) => ({
    name: row.categoryName,
    hours: hoursFromSeconds(row.durationSeconds),
  }));

  if (!projectId) {
    return <EmptyState message="Select a project to view its timesheet report." />;
  }
  if (report.isLoading || entries.isLoading || sessions.isLoading) {
    return <LoadingState label="Loading project report…" />;
  }
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (entries.error) {
    return (
      <ErrorState
        message={
          entries.error instanceof Error ? entries.error.message : 'Failed to load timesheet entries'
        }
      />
    );
  }
  if (sessions.error) {
    return (
      <ErrorState
        message={
          sessions.error instanceof Error ? sessions.error.message : 'Failed to load sessions'
        }
      />
    );
  }
  if (!report.data) return null;

  const projectIds = [projectId];
  const openRange = (patch: Partial<TimesheetEntriesDrilldown> & Pick<TimesheetEntriesDrilldown, 'title'>) =>
    onEntriesClick({ fromUtc, toUtc, projectIds, ...patch });

  return (
    <div className="stack">
      <p className="muted">
        {report.data.projectName}
        {report.data.clientName?.trim() ? ` · ${report.data.clientName}` : ''}
      </p>
      <TotalsCards
        totals={report.data.totals}
        onEntriesClick={() =>
          openRange({ title: `Timesheet entries · ${report.data.projectName}` })
        }
        onOpenEntriesClick={() =>
          openRange({
            title: `Open timesheet entries · ${report.data.projectName}`,
            openOnly: true,
          })
        }
      />
      <div className="chart-grid">
        {categoryChart.length > 0 ? (
          <ChartCard title="By category (hours)">
            <NamedPieChart data={categoryChart} nameKey="name" valueKey="hours" />
          </ChartCard>
        ) : null}
        <DailyChart rows={byDay} onDayClick={(day) => onDayClick(day, [projectId])} />
      </div>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable
          rows={report.data.byCategory}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.categoryName}`,
              categoryId: row.categoryId,
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable
          rows={byDay}
          onDayClick={(day) => onDayClick(day, [projectId])}
          onEntriesClick={(day) => {
            const bounds = dayBoundsLocal(day);
            openRange({
              title: `Timesheet entries on ${day}`,
              fromUtc: bounds.fromUtc,
              toUtc: bounds.toUtc,
              day,
            });
          }}
        />
      </section>
    </div>
  );
}

function ClientReportView({
  fromUtc,
  toUtc,
  clientName,
  onDayClick,
  onEntriesClick,
}: {
  fromUtc: string;
  toUtc: string;
  clientName: string;
  onDayClick: (day: string, projectIds?: string[]) => void;
  onEntriesClick: (drilldown: TimesheetEntriesDrilldown) => void;
}) {
  const report = useTimesheetClientReportQuery(
    clientName || undefined,
    fromUtc,
    toUtc,
    Boolean(clientName),
  );
  const entries = useTimesheetEntriesQuery({ fromUtc, toUtc }, Boolean(clientName));
  const sessions = useSessionsQuery({ fromUtc, toUtc }, Boolean(clientName));
  const clientProjectIds = useMemo(
    () => new Set((report.data?.byProject ?? []).map((row) => row.projectId)),
    [report.data?.byProject],
  );
  const clientEntries = useMemo(
    () => (entries.data ?? []).filter((entry) => clientProjectIds.has(entry.projectId)),
    [entries.data, clientProjectIds],
  );
  const clientSessions = useMemo(
    () =>
      (sessions.data ?? []).filter(
        (session) => session.projectId != null && clientProjectIds.has(session.projectId),
      ),
    [sessions.data, clientProjectIds],
  );
  const byDay = useMemo(
    () => buildLocalDailyBreakdown(clientEntries, fromUtc, toUtc, clientSessions),
    [clientEntries, clientSessions, fromUtc, toUtc],
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
  if (report.isLoading || entries.isLoading || sessions.isLoading) {
    return <LoadingState label="Loading client report…" />;
  }
  if (report.error) {
    return (
      <ErrorState
        message={report.error instanceof Error ? report.error.message : 'Failed to load report'}
      />
    );
  }
  if (entries.error) {
    return (
      <ErrorState
        message={
          entries.error instanceof Error ? entries.error.message : 'Failed to load timesheet entries'
        }
      />
    );
  }
  if (sessions.error) {
    return (
      <ErrorState
        message={
          sessions.error instanceof Error ? sessions.error.message : 'Failed to load sessions'
        }
      />
    );
  }
  if (!report.data) return null;

  const projectIds = report.data.byProject.map((row) => row.projectId);
  const openRange = (patch: Partial<TimesheetEntriesDrilldown> & Pick<TimesheetEntriesDrilldown, 'title'>) =>
    onEntriesClick({ fromUtc, toUtc, projectIds, ...patch });

  return (
    <div className="stack">
      <p className="muted">{report.data.clientName}</p>
      <TotalsCards
        totals={report.data.totals}
        onEntriesClick={() =>
          openRange({ title: `Timesheet entries · ${report.data.clientName}` })
        }
        onOpenEntriesClick={() =>
          openRange({
            title: `Open timesheet entries · ${report.data.clientName}`,
            openOnly: true,
          })
        }
      />
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
          rows={byDay}
          onDayClick={(day) => onDayClick(day, report.data?.byProject.map((row) => row.projectId))}
        />
      </div>
      <section className="page-section">
        <h3>By project</h3>
        <ProjectTable
          rows={report.data.byProject}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.projectName}`,
              projectIds: [row.projectId],
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By category</h3>
        <CategoryTable
          rows={report.data.byCategory}
          onEntriesClick={(row) =>
            openRange({
              title: `Timesheet entries · ${row.categoryName}`,
              categoryId: row.categoryId,
            })
          }
        />
      </section>
      <section className="page-section">
        <h3>By day</h3>
        <DailyTable
          rows={byDay}
          onDayClick={(day) => onDayClick(day, report.data?.byProject.map((row) => row.projectId))}
          onEntriesClick={(day) => {
            const bounds = dayBoundsLocal(day);
            openRange({
              title: `Timesheet entries on ${day}`,
              fromUtc: bounds.fromUtc,
              toUtc: bounds.toUtc,
              day,
            });
          }}
        />
      </section>
    </div>
  );
}

function TimesheetEntriesDialog({
  drilldown,
  projectNameById,
  onClose,
}: {
  drilldown: TimesheetEntriesDrilldown;
  projectNameById: Map<string, string>;
  onClose: () => void;
}) {
  const singleProjectId =
    drilldown.projectIds?.length === 1 ? drilldown.projectIds[0] : undefined;
  const entries = useTimesheetEntriesQuery(
    {
      projectId: singleProjectId,
      fromUtc: drilldown.fromUtc,
      toUtc: drilldown.toUtc,
    },
    true,
  );
  const allowedProjectIds = useMemo(
    () => new Set(drilldown.projectIds ?? []),
    [drilldown.projectIds],
  );

  const visibleEntries = useMemo(() => {
    return (entries.data ?? [])
      .filter((entry) => {
        if (
          allowedProjectIds.size > 0 &&
          !allowedProjectIds.has(entry.projectId)
        ) {
          return false;
        }
        if (drilldown.categoryId && entry.categoryId !== drilldown.categoryId) {
          return false;
        }
        if (drilldown.openOnly && !entry.isOpen) {
          return false;
        }
        if (drilldown.day && toLocalDayKey(entry.startedAtUtc) !== drilldown.day) {
          return false;
        }
        return true;
      })
      .sort((a, b) => b.startedAtUtc.localeCompare(a.startedAtUtc));
  }, [
    allowedProjectIds,
    drilldown.categoryId,
    drilldown.day,
    drilldown.openOnly,
    entries.data,
  ]);

  return (
    <PopupForm
      title={drilldown.title}
      subtitle={`${formatNumber(visibleEntries.length)} entr${visibleEntries.length === 1 ? 'y' : 'ies'}`}
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      {entries.isLoading ? (
        <LoadingState label="Loading timesheet entries…" />
      ) : entries.error ? (
        <ErrorState
          message={
            entries.error instanceof Error
              ? entries.error.message
              : 'Failed to load timesheet entries'
          }
        />
      ) : visibleEntries.length === 0 ? (
        <EmptyState message="No timesheet entries match this selection." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>Project</th>
                <th>Category</th>
                <th>Started</th>
                <th>Ended</th>
                <th>Duration</th>
                <th>Status</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              {visibleEntries.map((entry) => {
                const duration = timesheetEntryDurationMs(entry);
                return (
                  <tr key={entry.id}>
                    <td>
                      <TextLink to={`/projects/${entry.projectId}?tab=Timesheet`}>
                        {entry.projectName?.trim() ||
                          projectNameById.get(entry.projectId) ||
                          entry.projectId}
                      </TextLink>
                    </td>
                    <td>{entry.categoryName?.trim() ? entry.categoryName : '—'}</td>
                    <td>{formatDateTime(entry.startedAtUtc)}</td>
                    <td>{formatDateTime(entry.endedAtUtc)}</td>
                    <td>
                      {duration == null
                        ? '—'
                        : `${formatDurationMs(duration)}${entry.isOpen ? ' (running)' : ''}`}
                    </td>
                    <td>
                      <StatusBadge
                        label={entry.isOpen ? 'Open' : 'Closed'}
                        tone={entry.isOpen ? 'success' : 'neutral'}
                      />
                    </td>
                    <td>{entry.notes?.trim() ? entry.notes : '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </TablePanel>
      )}
    </PopupForm>
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
  const bounds = useMemo(() => dayBoundsLocal(day), [day]);
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
      if (!sessionOverlapsLocalDay(session, day)) {
        return false;
      }
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
  }, [allowedProjectIds, day, sessions.data, statusFilter]);

  const sessionStatusOptions = useMemo(() => {
    const values = new Set<string>();
    for (const session of sessions.data ?? []) {
      if (!sessionOverlapsLocalDay(session, day)) {
        continue;
      }
      if (
        allowedProjectIds.size > 0 &&
        !(session.projectId && allowedProjectIds.has(session.projectId))
      ) {
        continue;
      }
      values.add(session.status ?? (session.isActive ? 'Active' : '—'));
    }
    return [...values].sort((a, b) => a.localeCompare(b));
  }, [allowedProjectIds, day, sessions.data]);

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
      ) : (sessions.data ?? []).filter(
          (session) =>
            sessionOverlapsLocalDay(session, day) &&
            (allowedProjectIds.size === 0 ||
              (session.projectId != null && allowedProjectIds.has(session.projectId))),
        ).length === 0 ? (
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
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const selectedYear = parseYearParam(searchParams.get('year'));
  const selectedMonth = parseMonthParam(searchParams.get('month'));
  const projectId = searchParams.get('project') ?? '';
  const clientName = searchParams.get('client') ?? '';
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

  const projects = useProjectsQuery();
  const clients = useReportClientsQuery();
  const monthsQuery = useTimesheetReportMonthsQuery(
    scope === 'project' ? projectId || null : null,
    scope === 'client' ? clientName || null : null,
  );

  const [selectedDay, setSelectedDay] = useState<{
    day: string;
    projectIds?: string[];
  } | null>(null);
  const [selectedSession, setSelectedSession] = useState<SessionDto | null>(null);
  const [selectedEntries, setSelectedEntries] = useState<TimesheetEntriesDrilldown | null>(
    null,
  );

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

  const openDay = (day: string, projectIds?: string[]) => {
    setSelectedSession(null);
    setSelectedEntries(null);
    setSelectedDay({ day, projectIds: projectIds?.filter(Boolean) });
  };

  const openEntries = (drilldown: TimesheetEntriesDrilldown) => {
    setSelectedDay(null);
    setSelectedSession(null);
    setSelectedEntries(drilldown);
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
              Billable time by range, optionally filtered to one project or client. Open timesheet entries
              count through now within the selected range.
            </p>
          </div>
        </div>

        <Panel>
          <div className="field-row">
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
            idPrefix="timesheet-report-range"
          />
        </Panel>

        {scope === 'all' ? (
          <OverallReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            onDayClick={openDay}
            onClientClick={focusClient}
            onEntriesClick={openEntries}
          />
        ) : scope === 'project' ? (
          <ProjectReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            projectId={projectId}
            onDayClick={openDay}
            onEntriesClick={openEntries}
          />
        ) : (
          <ClientReportView
            fromUtc={range.fromUtc}
            toUtc={range.toUtc}
            clientName={clientName}
            onDayClick={openDay}
            onEntriesClick={openEntries}
          />
        )}
      </section>
      {selectedEntries ? (
        <TimesheetEntriesDialog
          drilldown={selectedEntries}
          projectNameById={projectNameById}
          onClose={() => setSelectedEntries(null)}
        />
      ) : null}
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
