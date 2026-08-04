import { useMemo, useState, type FormEvent } from 'react';
import type {
  BrowseCalendarScope,
  BrowseViewMode,
} from '@lunarq/frontend-shared/components';
import { exportToExcel } from '@lunarq/frontend-shared/utils';
import {
  useCreateTimesheetEntryMutation,
  useDeleteTimesheetEntryMutation,
  useEndTimesheetMutation,
  useProjectsQuery,
  useStartTimesheetMutation,
  useTimesheetCategoriesQuery,
  useTimesheetEntriesQuery,
  useUpdateTimesheetEntryMutation,
} from '../api/hooks';
import { api } from '../api/client';
import type { TimesheetEntryDto } from '../api/types';
import { DateTimeField, isCompleteLocalDateTime } from '../components/DateTimeField';
import { RemoteAnalysisDetailBrowse } from '../components/RemoteAnalysisDetailBrowse';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { BrowseListControls, PopupForm, TextLink } from '../shared/adminUi';
import { type RangePreset, resolveRange } from '../utils/dateRange';
import { timesheetEntryDurationMs } from '../utils/duration';
import { formatDateTime, formatDurationMs, formatNumber } from '../utils/format';

type TimesheetDraft = {
  projectId: string;
  categoryId: string;
  startedAtLocal: string;
  endedAtLocal: string;
  notes: string;
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

function emptyDraft(defaultCategoryId = '', projectId = ''): TimesheetDraft {
  return {
    projectId,
    categoryId: defaultCategoryId,
    startedAtLocal: toLocalInputValue(new Date().toISOString()),
    endedAtLocal: '',
    notes: '',
  };
}

function draftFromEntry(entry: TimesheetEntryDto): TimesheetDraft {
  return {
    projectId: entry.projectId,
    categoryId: entry.categoryId,
    startedAtLocal: toLocalInputValue(entry.startedAtUtc),
    endedAtLocal: toLocalInputValue(entry.endedAtUtc),
    notes: entry.notes ?? '',
  };
}

function defaultCategoryId(
  categories: { id: string; name: string }[] | undefined,
): string {
  return (
    categories?.find((c) => c.name.toLowerCase() === 'work')?.id ??
    categories?.[0]?.id ??
    ''
  );
}

function toDayKey(iso: string): string | null {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function formatDayKey(dayKey: string): string {
  const d = new Date(`${dayKey}T12:00:00`);
  if (Number.isNaN(d.getTime())) return dayKey;
  return d.toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

function shiftDayKey(dayKey: string, deltaDays: number): string {
  const d = new Date(`${dayKey}T12:00:00`);
  d.setDate(d.getDate() + deltaDays);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function TimesheetPage() {
  const [rangePreset, setRangePreset] = useState<RangePreset>('30d');
  const range = useMemo(() => resolveRange(rangePreset), [rangePreset]);
  const [projectFilter, setProjectFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [searchValue, setSearchValue] = useState('');
  const [viewMode, setViewMode] = useState<BrowseViewMode>('grid');
  const [calendarScope, setCalendarScope] = useState<BrowseCalendarScope>('day');
  const [calendarCursor, setCalendarCursor] = useState(() => new Date());
  const [selectedDayKey, setSelectedDayKey] = useState<string | null>(() =>
    toDayKey(new Date().toISOString()),
  );
  const [browseEpoch, setBrowseEpoch] = useState(0);

  const projects = useProjectsQuery();
  const timesheetCategories = useTimesheetCategoriesQuery(true);
  const calendarMode = viewMode === 'calendar';
  const entries = useTimesheetEntriesQuery(
    {
      projectId: projectFilter || undefined,
      fromUtc: range.fromUtc,
      toUtc: range.toUtc,
    },
    calendarMode,
  );

  const createMutation = useCreateTimesheetEntryMutation();
  const updateMutation = useUpdateTimesheetEntryMutation();
  const deleteMutation = useDeleteTimesheetEntryMutation();
  const startMutation = useStartTimesheetMutation();
  const endMutation = useEndTimesheetMutation();

  const [editorOpen, setEditorOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<TimesheetDraft>(emptyDraft);
  const [message, setMessage] = useState<string | null>(null);
  const [startOpen, setStartOpen] = useState(false);
  const [startProjectId, setStartProjectId] = useState('');
  const [startCategoryId, setStartCategoryId] = useState('');

  const activeProjects = (projects.data ?? []).filter((p) => p.isActive !== false);
  const projectNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of projects.data ?? []) {
      map.set(p.id, p.name);
    }
    return map;
  }, [projects.data]);

  const sourceEntries = useMemo(
    () => (Array.isArray(entries.data) ? entries.data : []),
    [entries.data],
  );

  const filteredEntries = useMemo(() => {
    const query = searchValue.trim().toLowerCase();
    return sourceEntries.filter((entry) => {
      if (statusFilter === 'open' && !entry.isOpen) return false;
      if (statusFilter === 'closed' && entry.isOpen) return false;

      if (!query) return true;

      const projectLabel =
        entry.projectName?.trim() ||
        projectNameById.get(entry.projectId) ||
        entry.projectId;
      const haystack = [
        projectLabel,
        entry.categoryName ?? '',
        entry.notes ?? '',
        entry.isOpen ? 'open' : 'closed',
        formatDateTime(entry.startedAtUtc),
        formatDateTime(entry.endedAtUtc),
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(query);
    });
  }, [projectNameById, searchValue, sourceEntries, statusFilter]);

  const entriesByDay = useMemo(() => {
    const map = new Map<string, TimesheetEntryDto[]>();
    for (const entry of filteredEntries) {
      const dayKey = toDayKey(entry.startedAtUtc);
      if (!dayKey) continue;
      const list = map.get(dayKey) ?? [];
      list.push(entry);
      map.set(dayKey, list);
    }
    return map;
  }, [filteredEntries]);

  const calendarYear = calendarCursor.getFullYear();
  const calendarMonth = calendarCursor.getMonth();
  const monthLabel = calendarCursor.toLocaleDateString(undefined, {
    month: 'long',
    year: 'numeric',
  });

  const monthGrid = useMemo(() => {
    const firstDayIndex = new Date(calendarYear, calendarMonth, 1).getDay();
    const daysInMonth = new Date(calendarYear, calendarMonth + 1, 0).getDate();
    const cells: Array<number | null> = [
      ...Array.from({ length: firstDayIndex }, () => null),
      ...Array.from({ length: daysInMonth }, (_, dayIndex) => dayIndex + 1),
    ];
    const totalCells = Math.ceil(cells.length / 7) * 7;
    return [
      ...cells,
      ...Array.from({ length: totalCells - cells.length }, () => null),
    ];
  }, [calendarMonth, calendarYear]);

  const yearMonthCounts = useMemo(() => {
    const counts = Array.from({ length: 12 }, () => 0);
    for (const entry of filteredEntries) {
      const started = new Date(entry.startedAtUtc);
      if (Number.isNaN(started.getTime()) || started.getFullYear() !== calendarYear) {
        continue;
      }
      counts[started.getMonth()] += 1;
    }
    return counts;
  }, [calendarYear, filteredEntries]);

  const openEditor = (entry?: TimesheetEntryDto) => {
    if (entry) {
      setEditingId(entry.id);
      setDraft(draftFromEntry(entry));
    } else {
      setEditingId(null);
      setDraft(
        emptyDraft(
          defaultCategoryId(timesheetCategories.data),
          projectFilter || activeProjects[0]?.id || '',
        ),
      );
    }
    setMessage(null);
    setEditorOpen(true);
    setStartOpen(false);
  };

  const openStart = () => {
    setStartProjectId(projectFilter || activeProjects[0]?.id || '');
    setStartCategoryId(defaultCategoryId(timesheetCategories.data));
    setStartOpen(true);
    setEditorOpen(false);
    setMessage(null);
  };

  function projectLabelFor(entry: TimesheetEntryDto): string {
    return (
      entry.projectName?.trim() ||
      projectNameById.get(entry.projectId) ||
      entry.projectId
    );
  }

  function renderEntryActions(entry: TimesheetEntryDto) {
    return (
      <div className="row-actions">
        {entry.isOpen ? (
          <button
            type="button"
            className="btn btn-compact"
            disabled={endMutation.isPending}
            onClick={() => {
              void endMutation
                .mutateAsync({ timesheetEntryId: entry.id })
                .then(() => {
                  setMessage('Timesheet ended.');
                  setBrowseEpoch((value) => value + 1);
                  return entries.refetch();
                })
                .catch((err: unknown) => {
                  setMessage(err instanceof Error ? err.message : 'End failed');
                });
            }}
          >
            End
          </button>
        ) : null}
        <button
          type="button"
          className="btn btn-compact btn-secondary"
          onClick={() => openEditor(entry)}
        >
          Edit
        </button>
        <button
          type="button"
          className="btn btn-compact btn-danger"
          disabled={deleteMutation.isPending}
          onClick={() => {
            const ok = window.confirm('Delete this timesheet entry?');
            if (!ok) return;
            void deleteMutation
              .mutateAsync({ id: entry.id, projectId: entry.projectId })
              .then(() => {
                setMessage(null);
                setBrowseEpoch((value) => value + 1);
                return entries.refetch();
              })
              .catch((err: unknown) => {
                setMessage(err instanceof Error ? err.message : 'Delete failed');
              });
          }}
        >
          Delete
        </button>
      </div>
    );
  }

  async function onExportToExcel() {
    await exportToExcel({
      filename: 'timesheet-entries.xlsx',
      title: 'Timesheet entries',
      timestamp: new Date().toISOString(),
      columns: [
        { header: 'Project', key: 'project' },
        { header: 'Category', key: 'category' },
        { header: 'Started', key: 'started' },
        { header: 'Ended', key: 'ended' },
        { header: 'Duration (ms)', key: 'durationMs' },
        { header: 'Notes', key: 'notes' },
        { header: 'Status', key: 'status' },
      ],
      data: filteredEntries.map((entry) => ({
        project: projectLabelFor(entry),
        category: entry.categoryName?.trim() || '',
        started: entry.startedAtUtc,
        ended: entry.endedAtUtc ?? '',
        durationMs: timesheetEntryDurationMs(entry) ?? '',
        notes: entry.notes?.trim() || '',
        status: entry.isOpen ? 'Open' : 'Closed',
      })),
    });
  }

  function openCalendarDay(dayOfMonth: number) {
    const pad = (n: number) => String(n).padStart(2, '0');
    const dayKey = `${calendarYear}-${pad(calendarMonth + 1)}-${pad(dayOfMonth)}`;
    setSelectedDayKey(dayKey);
    setCalendarScope('day');
  }

  function renderEntryCard(entry: TimesheetEntryDto) {
    const duration = timesheetEntryDurationMs(entry);
    return (
      <article key={entry.id} className="timesheet-browse-tile">
        <div className="timesheet-browse-tile-header">
          <TextLink to={`/projects/${entry.projectId}?tab=Timesheet`}>
            {projectLabelFor(entry)}
          </TextLink>
          <StatusBadge
            label={entry.isOpen ? 'Open' : 'Closed'}
            tone={entry.isOpen ? 'success' : 'neutral'}
          />
        </div>
        <p>{entry.categoryName?.trim() ? entry.categoryName : 'No category'}</p>
        <dl className="timesheet-browse-tile-stats">
          <div>
            <dt>Started</dt>
            <dd>{formatDateTime(entry.startedAtUtc)}</dd>
          </div>
          <div>
            <dt>Ended</dt>
            <dd>{formatDateTime(entry.endedAtUtc)}</dd>
          </div>
          <div>
            <dt>Duration</dt>
            <dd>
              {duration == null
                ? '—'
                : `${formatDurationMs(duration)}${entry.isOpen ? ' (running)' : ''}`}
            </dd>
          </div>
        </dl>
        {entry.notes?.trim() ? (
          <p className="timesheet-browse-tile-notes">{entry.notes}</p>
        ) : null}
        {renderEntryActions(entry)}
      </article>
    );
  }

  function renderCalendar() {
    const todayKey = toDayKey(new Date().toISOString());

    if (calendarScope === 'year') {
      return (
        <div className="timesheet-calendar">
          <div className="timesheet-calendar-header">
            <div>
              <strong>{calendarYear}</strong>
              <span>{formatNumber(filteredEntries.length)} entries</span>
            </div>
            <div className="timesheet-calendar-nav" role="group" aria-label="Calendar year navigation">
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => setCalendarCursor(new Date(calendarYear - 1, 0, 1))}
              >
                Previous
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => setCalendarCursor(new Date())}
              >
                This year
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => setCalendarCursor(new Date(calendarYear + 1, 0, 1))}
              >
                Next
              </button>
            </div>
          </div>
          <div className="timesheet-calendar-year-grid">
            {yearMonthCounts.map((count, monthIndex) => {
              const label = new Date(calendarYear, monthIndex, 1).toLocaleDateString(undefined, {
                month: 'long',
              });
              return (
                <button
                  key={label}
                  type="button"
                  className={`timesheet-calendar-month-card${count > 0 ? ' has-items' : ''}`}
                  onClick={() => {
                    setCalendarCursor(new Date(calendarYear, monthIndex, 1));
                    setCalendarScope('month');
                    setSelectedDayKey(null);
                  }}
                >
                  <strong>{label}</strong>
                  <span>
                    {formatNumber(count)} entr{count === 1 ? 'y' : 'ies'}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      );
    }

    if (calendarScope === 'day') {
      const dayKey =
        selectedDayKey ??
        [...entriesByDay.keys()].sort().at(-1) ??
        todayKey ??
        `${calendarYear}-${String(calendarMonth + 1).padStart(2, '0')}-01`;
      const dayEntries = entriesByDay.get(dayKey) ?? [];

      return (
        <div className="timesheet-calendar">
          <div className="timesheet-calendar-header">
            <div>
              <strong>{formatDayKey(dayKey)}</strong>
              <span>
                {formatNumber(dayEntries.length)} entr{dayEntries.length === 1 ? 'y' : 'ies'}
              </span>
            </div>
            <div className="timesheet-calendar-nav" role="group" aria-label="Calendar day navigation">
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => setSelectedDayKey(shiftDayKey(dayKey, -1))}
              >
                Previous
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => {
                  setSelectedDayKey(null);
                  setCalendarScope('month');
                  const parts = dayKey.split('-').map(Number);
                  if (parts.length === 3) {
                    setCalendarCursor(new Date(parts[0], parts[1] - 1, 1));
                  }
                }}
              >
                Month
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => {
                  if (todayKey) setSelectedDayKey(todayKey);
                }}
              >
                Today
              </button>
              <button
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={() => setSelectedDayKey(shiftDayKey(dayKey, 1))}
              >
                Next
              </button>
            </div>
          </div>
          {dayEntries.length === 0 ? (
            <EmptyState message="No timesheet entries on this day." />
          ) : (
            <div className="timesheet-browse-grid">{dayEntries.map(renderEntryCard)}</div>
          )}
        </div>
      );
    }

    return (
      <div className="timesheet-calendar">
        <div className="timesheet-calendar-header">
          <div>
            <strong>{monthLabel}</strong>
            <span>{formatNumber(filteredEntries.length)} entries in view</span>
          </div>
          <div className="timesheet-calendar-nav" role="group" aria-label="Calendar month navigation">
            <button
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={() =>
                setCalendarCursor(new Date(calendarYear, calendarMonth - 1, 1))
              }
            >
              Previous
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={() => {
                setCalendarCursor(new Date());
                setSelectedDayKey(null);
              }}
            >
              Today
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={() =>
                setCalendarCursor(new Date(calendarYear, calendarMonth + 1, 1))
              }
            >
              Next
            </button>
          </div>
        </div>
        <div className="timesheet-calendar-weekdays" aria-hidden="true">
          <span>Sun</span>
          <span>Mon</span>
          <span>Tue</span>
          <span>Wed</span>
          <span>Thu</span>
          <span>Fri</span>
          <span>Sat</span>
        </div>
        <div className="timesheet-calendar-grid" role="grid" aria-label={`${monthLabel} calendar`}>
          {Array.from({ length: Math.ceil(monthGrid.length / 7) }, (_, rowIndex) => {
            const rowCells = monthGrid.slice(rowIndex * 7, rowIndex * 7 + 7);
            return (
              <div key={`calendar-row-${rowIndex}`} className="timesheet-calendar-row" role="row">
                {rowCells.map((day, cellOffset) => {
                  if (day == null) {
                    return (
                      <div
                        key={`empty-${rowIndex}-${cellOffset}`}
                        className="timesheet-calendar-cell is-empty"
                      />
                    );
                  }
                  const pad = (n: number) => String(n).padStart(2, '0');
                  const dayKey = `${calendarYear}-${pad(calendarMonth + 1)}-${pad(day)}`;
                  const dayEntries = entriesByDay.get(dayKey) ?? [];
                  const isToday = dayKey === todayKey;
                  return (
                    <button
                      key={dayKey}
                      type="button"
                      className={[
                        'timesheet-calendar-cell-button',
                        dayEntries.length > 0 ? 'has-items' : '',
                        isToday ? 'is-today' : '',
                      ]
                        .filter(Boolean)
                        .join(' ')}
                      onClick={() => openCalendarDay(day)}
                    >
                      <span className="timesheet-calendar-day">{day}</span>
                      {dayEntries.slice(0, 2).map((entry) => (
                        <span key={entry.id} className="timesheet-calendar-event">
                          {projectLabelFor(entry)}
                        </span>
                      ))}
                      {dayEntries.length > 2 ? (
                        <span className="timesheet-calendar-more">
                          +{dayEntries.length - 2} more
                        </span>
                      ) : null}
                    </button>
                  );
                })}
              </div>
            );
          })}
        </div>
      </div>
    );
  }

  return (
    <>
      <section className="page-section">
        <p className="muted">
          Start and end billable time, or add closed entries. MCP tools{' '}
          <code>start_timesheet</code> / <code>end_timesheet</code> write here too. Categories live
          under Settings → Data.
        </p>

        {calendarMode ? (
          <BrowseListControls
            heading="Entries"
            viewMode={viewMode}
            onViewModeChange={(next) => {
              setViewMode(next);
              if (next === 'calendar') {
                const todayKey = toDayKey(new Date().toISOString());
                setCalendarCursor(new Date());
                setCalendarScope('day');
                setSelectedDayKey(todayKey);
              }
            }}
            allowCalendarView
            calendarScope={calendarScope}
            onCalendarScopeChange={(next) => {
              setCalendarScope(next);
              if (next === 'day') {
                setSelectedDayKey((current) => current ?? toDayKey(new Date().toISOString()));
              } else {
                setSelectedDayKey(null);
              }
            }}
            searchValue={searchValue}
            searchPlaceholder="Search entries…"
            onSearchChange={setSearchValue}
            onExportToExcel={() => void onExportToExcel()}
            exportLabel="Export to Excel"
            exportDisabled={filteredEntries.length === 0}
            filters={[
              {
                id: 'timesheet-range',
                label: 'Range',
                value: rangePreset,
                onChange: (value) => setRangePreset(value as RangePreset),
                options: [
                  { value: '7d', label: 'Last 7 days' },
                  { value: '30d', label: 'Last 30 days' },
                  { value: '90d', label: 'Last 90 days' },
                  { value: 'month', label: 'This month' },
                ],
              },
              {
                id: 'timesheet-project-filter',
                label: 'Project',
                value: projectFilter,
                onChange: setProjectFilter,
                options: [
                  { value: '', label: 'All projects' },
                  ...(projects.data ?? []).map((p) => ({
                    value: p.id,
                    label: p.name,
                  })),
                ],
              },
              {
                id: 'timesheet-status-filter',
                label: 'Status',
                value: statusFilter,
                onChange: setStatusFilter,
                options: [
                  { value: '', label: 'All statuses' },
                  { value: 'open', label: 'Open' },
                  { value: 'closed', label: 'Closed' },
                ],
              },
            ]}
            customControls={[
              <button
                key="start-timer"
                type="button"
                className="btn btn-secondary"
                onClick={openStart}
              >
                Start timer
              </button>,
              <button key="add-entry" type="button" className="btn" onClick={() => openEditor()}>
                Add entry
              </button>,
            ]}
          />
        ) : null}

        {calendarMode ? (
          <p className="section-meta">
            Showing {formatNumber(filteredEntries.length)} of {formatNumber(sourceEntries.length)}{' '}
            entries · {range.label}
            {projectFilter ? ` · project filter` : ''}
            {statusFilter ? ` · status=${statusFilter}` : ''}
          </p>
        ) : null}

        {startOpen ? (
          <PopupForm
            title="Start timer"
            contentClassName="popup-form--narrow"
            onClose={() => {
              setStartOpen(false);
              setMessage(null);
            }}
            onSubmit={(e) => {
              const event = e as FormEvent;
              event.preventDefault();
              void (async () => {
                setMessage(null);
                if (!startProjectId) {
                  setMessage('Project is required to start a timesheet.');
                  return;
                }
                try {
                  await startMutation.mutateAsync({
                    projectId: startProjectId,
                    categoryId: startCategoryId || null,
                  });
                  setMessage('Timesheet started. Any other open timer was closed.');
                  setStartOpen(false);
                  setBrowseEpoch((value) => value + 1);
                  await entries.refetch();
                } catch (err) {
                  setMessage(err instanceof Error ? err.message : 'Start failed');
                }
              })();
            }}
            footer={
              <>
                <button type="submit" className="btn" disabled={startMutation.isPending}>
                  {startMutation.isPending ? 'Starting…' : 'Start'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => {
                    setStartOpen(false);
                    setMessage(null);
                  }}
                >
                  Cancel
                </button>
              </>
            }
          >
            <div className="field-row">
              <div className="field">
                <label htmlFor="start-project">Project</label>
                <select
                  id="start-project"
                  required
                  value={startProjectId}
                  onChange={(e) => setStartProjectId(e.target.value)}
                >
                  <option value="" disabled>
                    Select project…
                  </option>
                  {activeProjects.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor="start-category">Category</label>
                <select
                  id="start-category"
                  value={startCategoryId}
                  onChange={(e) => setStartCategoryId(e.target.value)}
                >
                  {(timesheetCategories.data ?? []).map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            {message ? <p className="form-message">{message}</p> : null}
          </PopupForm>
        ) : null}

        {editorOpen ? (
          <PopupForm
            title={editingId ? 'Edit timesheet entry' : 'New timesheet entry'}
            onClose={() => {
              setEditorOpen(false);
              setEditingId(null);
              setMessage(null);
            }}
            onSubmit={(e) => {
              const event = e as FormEvent;
              event.preventDefault();
              void (async () => {
                setMessage(null);
                if (!editingId && !draft.projectId) {
                  setMessage('Project is required.');
                  return;
                }
                if (!isCompleteLocalDateTime(draft.startedAtLocal)) {
                  setMessage('Started date and time are required.');
                  return;
                }
                const startedAtUtc = fromLocalInputValue(draft.startedAtLocal);
                if (!startedAtUtc) {
                  setMessage('Started date and time are invalid.');
                  return;
                }
                if (draft.endedAtLocal.trim() && !isCompleteLocalDateTime(draft.endedAtLocal)) {
                  setMessage('Ended date and time are incomplete.');
                  return;
                }
                const endedAtUtc = fromLocalInputValue(draft.endedAtLocal);
                if (
                  endedAtUtc &&
                  new Date(endedAtUtc).getTime() < new Date(startedAtUtc).getTime()
                ) {
                  setMessage('Ended time cannot be earlier than started time.');
                  return;
                }
                if (!draft.categoryId) {
                  setMessage('Category is required.');
                  return;
                }
                try {
                  if (editingId) {
                    await updateMutation.mutateAsync({
                      id: editingId,
                      body: {
                        categoryId: draft.categoryId,
                        startedAtUtc,
                        endedAtUtc,
                        notes: draft.notes.trim() || null,
                      },
                    });
                    setMessage('Timesheet entry updated.');
                  } else {
                    await createMutation.mutateAsync({
                      projectId: draft.projectId,
                      body: {
                        categoryId: draft.categoryId,
                        startedAtUtc,
                        endedAtUtc,
                        notes: draft.notes.trim() || null,
                      },
                    });
                    setMessage('Timesheet entry created.');
                  }
                  setEditorOpen(false);
                  setEditingId(null);
                  setBrowseEpoch((value) => value + 1);
                  await entries.refetch();
                } catch (err) {
                  setMessage(err instanceof Error ? err.message : 'Save failed');
                }
              })();
            }}
            footer={
              <>
                <button
                  type="submit"
                  className="btn"
                  disabled={createMutation.isPending || updateMutation.isPending}
                >
                  {createMutation.isPending || updateMutation.isPending
                    ? 'Saving…'
                    : editingId
                      ? 'Save entry'
                      : 'Create entry'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => {
                    setEditorOpen(false);
                    setEditingId(null);
                    setMessage(null);
                  }}
                >
                  Cancel
                </button>
              </>
            }
          >
            <div className="stack">
              {!editingId ? (
                <div className="field">
                  <label htmlFor="timesheet-project">Project</label>
                  <select
                    id="timesheet-project"
                    required
                    value={draft.projectId}
                    onChange={(e) => setDraft((s) => ({ ...s, projectId: e.target.value }))}
                  >
                    <option value="" disabled>
                      Select project…
                    </option>
                    {activeProjects.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </select>
                </div>
              ) : null}
              <div className="field">
                <label htmlFor="timesheet-category">Category</label>
                <select
                  id="timesheet-category"
                  required
                  value={draft.categoryId}
                  onChange={(e) => setDraft((s) => ({ ...s, categoryId: e.target.value }))}
                >
                  <option value="" disabled>
                    Select category…
                  </option>
                  {(timesheetCategories.data ?? []).map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                  {editingId &&
                  draft.categoryId &&
                  !(timesheetCategories.data ?? []).some((c) => c.id === draft.categoryId) ? (
                    <option value={draft.categoryId}>
                      {entries.data?.find((e) => e.id === editingId)?.categoryName ??
                        'Inactive category'}
                    </option>
                  ) : null}
                </select>
              </div>
              <div className="field-row">
                <DateTimeField
                  id="timesheet-started"
                  label="Started"
                  required
                  value={draft.startedAtLocal}
                  onChange={(startedAtLocal) => setDraft((s) => ({ ...s, startedAtLocal }))}
                />
                <DateTimeField
                  id="timesheet-ended"
                  label="Ended"
                  value={draft.endedAtLocal}
                  onChange={(endedAtLocal) => setDraft((s) => ({ ...s, endedAtLocal }))}
                />
              </div>
              <div className="field">
                <label htmlFor="timesheet-notes">Notes</label>
                <textarea
                  id="timesheet-notes"
                  value={draft.notes}
                  onChange={(e) => setDraft((s) => ({ ...s, notes: e.target.value }))}
                  rows={4}
                />
              </div>
              {message ? <p className="form-message">{message}</p> : null}
            </div>
          </PopupForm>
        ) : null}

        {!editorOpen && !startOpen && message ? (
          <p className="form-message">{message}</p>
        ) : null}

        {calendarMode ? (
          entries.isLoading || projects.isLoading ? (
            <LoadingState label="Loading timesheet…" />
          ) : entries.error ? (
            <ErrorState
              message={entries.error instanceof Error ? entries.error.message : 'Failed to load'}
            />
          ) : sourceEntries.length === 0 ? (
            <EmptyState message={`No timesheet entries in ${range.label.toLowerCase()}.`} />
          ) : filteredEntries.length === 0 ? (
            <EmptyState message="No timesheet entries match the current search or filters." />
          ) : (
            renderCalendar()
          )
        ) : (
          <RemoteAnalysisDetailBrowse<TimesheetEntryDto>
            embedded
            heading="Entries"
            searchPlaceholder="Search entries…"
            filterKey={[
              range.fromUtc,
              range.toUtc,
              projectFilter,
              browseEpoch,
            ].join('|')}
            allowCalendarView
            onRequestCalendarView={() => {
              const todayKey = toDayKey(new Date().toISOString());
              setCalendarCursor(new Date());
              setCalendarScope('day');
              setSelectedDayKey(todayKey);
              setViewMode('calendar');
            }}
            fetchPage={async ({ pageIndex, pageSize, search, status, signal }) =>
              api.getTimesheetEntriesPaged(
                {
                  projectId: projectFilter || undefined,
                  fromUtc: range.fromUtc,
                  toUtc: range.toUtc,
                  pageIndex,
                  pageSize,
                  search: search || undefined,
                  openClosed:
                    status === 'Open'
                      ? 'open'
                      : status === 'Closed'
                        ? 'closed'
                        : status === 'open' || status === 'closed'
                          ? status
                          : undefined,
                },
                signal,
              )
            }
            getStatusValue={(entry) => (entry.isOpen ? 'Open' : 'Closed')}
            statusOptions={[
              { value: 'Open', label: 'Open' },
              { value: 'Closed', label: 'Closed' },
            ]}
            filters={[
              {
                id: 'timesheet-range',
                label: 'Range',
                value: rangePreset,
                onChange: (value) => setRangePreset(value as RangePreset),
                options: [
                  { value: '7d', label: 'Last 7 days' },
                  { value: '30d', label: 'Last 30 days' },
                  { value: '90d', label: 'Last 90 days' },
                  { value: 'month', label: 'This month' },
                ],
              },
              {
                id: 'timesheet-project-filter',
                label: 'Project',
                value: projectFilter,
                onChange: setProjectFilter,
                options: [
                  { value: '', label: 'All projects' },
                  ...(projects.data ?? []).map((p) => ({
                    value: p.id,
                    label: p.name,
                  })),
                ],
              },
            ]}
            customControls={[
              <button
                key="start-timer"
                type="button"
                className="btn btn-secondary"
                onClick={openStart}
              >
                Start timer
              </button>,
              <button key="add-entry" type="button" className="btn" onClick={() => openEditor()}>
                Add entry
              </button>,
            ]}
            exportFilename="timesheet-entries.xlsx"
            exportTitle="Timesheet entries"
            exportColumns={[
              { header: 'Project', key: 'projectName' },
              { header: 'Category', key: 'categoryName' },
              { header: 'Started', key: 'startedAtUtc' },
              { header: 'Ended', key: 'endedAtUtc' },
              { header: 'Notes', key: 'notes' },
              { header: 'Status', key: 'status' },
            ]}
            toExportRow={(entry) => ({
              projectName: projectLabelFor(entry),
              categoryName: entry.categoryName ?? '',
              startedAtUtc: formatDateTime(entry.startedAtUtc),
              endedAtUtc: formatDateTime(entry.endedAtUtc),
              notes: entry.notes ?? '',
              status: entry.isOpen ? 'Open' : 'Closed',
            })}
            emptySourceMessage={`No timesheet entries in ${range.label.toLowerCase()}.`}
            emptyMessage="No timesheet entries match the current search or filters."
            renderTable={(rows) => (
              <table className="data">
                <thead>
                  <tr>
                    <th>Project</th>
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
                    const duration = timesheetEntryDurationMs(entry);
                    return (
                      <tr key={entry.id}>
                        <td>
                          <TextLink to={`/projects/${entry.projectId}?tab=Timesheet`}>
                            {projectLabelFor(entry)}
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
                        <td>{entry.notes?.trim() ? entry.notes : '—'}</td>
                        <td>
                          <StatusBadge
                            label={entry.isOpen ? 'Open' : 'Closed'}
                            tone={entry.isOpen ? 'success' : 'neutral'}
                          />
                        </td>
                        <td>{renderEntryActions(entry)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
            renderGrid={(rows) => rows.map(renderEntryCard)}
          />
        )}
      </section>
    </>
  );
}
