import { useMemo, useState } from 'react';
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
import type { TimesheetEntryDto } from '../api/types';
import { DateTimeField, isCompleteLocalDateTime } from '../components/DateTimeField';
import { Panel, TablePanel } from '../components/MetricCard';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { TextLink } from '../shared/adminUi';
import { type RangePreset, resolveRange } from '../utils/dateRange';
import { formatDateTime, formatDurationMs } from '../utils/format';

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

function entryDurationMs(entry: TimesheetEntryDto): number | null {
  const start = new Date(entry.startedAtUtc).getTime();
  if (Number.isNaN(start)) return null;
  const end = entry.endedAtUtc
    ? new Date(entry.endedAtUtc).getTime()
    : Date.now();
  if (Number.isNaN(end) || end < start) return null;
  return end - start;
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

export function TimesheetPage() {
  const [rangePreset, setRangePreset] = useState<RangePreset>('30d');
  const range = useMemo(() => resolveRange(rangePreset), [rangePreset]);
  const [projectFilter, setProjectFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const projects = useProjectsQuery();
  const timesheetCategories = useTimesheetCategoriesQuery(true);
  const entries = useTimesheetEntriesQuery({
    projectId: projectFilter || undefined,
    fromUtc: range.fromUtc,
    toUtc: range.toUtc,
  });

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

  const filteredEntries = useMemo(() => {
    const list = Array.isArray(entries.data) ? entries.data : [];
    if (!statusFilter) {
      return list;
    }
    return list.filter((entry) =>
      statusFilter === 'open' ? entry.isOpen : !entry.isOpen,
    );
  }, [entries.data, statusFilter]);

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

  return (
    <>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Entries</h2>
            <p className="muted">
              Start and end billable time, or add closed entries. MCP tools{' '}
              <code>start_timesheet</code> / <code>end_timesheet</code> write here too. Categories
              live under Settings → Data.
            </p>
          </div>
          <div className="row-actions">
            <button type="button" className="btn btn-secondary" onClick={openStart}>
              Start timer
            </button>
            <button type="button" className="btn" onClick={() => openEditor()}>
              Add entry
            </button>
          </div>
        </div>

        <Panel className="field-row">
          <div className="field">
            <label htmlFor="timesheet-range">Range</label>
            <select
              id="timesheet-range"
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
            <label htmlFor="timesheet-project-filter">Project</label>
            <select
              id="timesheet-project-filter"
              value={projectFilter}
              onChange={(e) => setProjectFilter(e.target.value)}
            >
              <option value="">All projects</option>
              {(projects.data ?? []).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="timesheet-status-filter">Status</label>
            <select
              id="timesheet-status-filter"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">All statuses</option>
              <option value="open">Open</option>
              <option value="closed">Closed</option>
            </select>
          </div>
        </Panel>

        {startOpen ? (
          <Panel className="stack">
          <form
            className="stack"
            noValidate
            onSubmit={async (event) => {
              event.preventDefault();
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
                await entries.refetch();
              } catch (err) {
                setMessage(err instanceof Error ? err.message : 'Start failed');
              }
            }}
          >
            <h3>Start timer</h3>
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
            <div className="row-actions">
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
              {message ? <span className="form-message">{message}</span> : null}
            </div>
          </form>
          </Panel>
        ) : null}

        {editorOpen ? (
          <Panel className="stack">
          <form
            className="stack"
            noValidate
            onSubmit={async (event) => {
              event.preventDefault();
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
                await entries.refetch();
              } catch (err) {
                setMessage(err instanceof Error ? err.message : 'Save failed');
              }
            }}
          >
            <h3>{editingId ? 'Edit timesheet entry' : 'New timesheet entry'}</h3>
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
            <div className="row-actions">
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
              {message ? <span className="form-message">{message}</span> : null}
            </div>
          </form>
          </Panel>
        ) : null}

        {!editorOpen && !startOpen && message ? (
          <p className="form-message">{message}</p>
        ) : null}

        {entries.isLoading || projects.isLoading ? (
          <LoadingState label="Loading timesheet…" />
        ) : entries.error ? (
          <ErrorState
            message={entries.error instanceof Error ? entries.error.message : 'Failed to load'}
          />
        ) : !Array.isArray(entries.data) || entries.data.length === 0 ? (
          <EmptyState message={`No timesheet entries in ${range.label.toLowerCase()}.`} />
        ) : filteredEntries.length === 0 ? (
          <EmptyState message="No timesheet entries match the current status filter." />
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
                  <th>Notes</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredEntries.map((entry) => {
                  const projectLabel =
                    entry.projectName?.trim() ||
                    projectNameById.get(entry.projectId) ||
                    entry.projectId;
                  const duration = entryDurationMs(entry);
                  return (
                    <tr key={entry.id}>
                      <td>
                        <TextLink to={`/projects/${entry.projectId}?tab=Timesheet`}>
                          {projectLabel}
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
                      <td>
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
                                    return entries.refetch();
                                  })
                                  .catch((err: unknown) => {
                                    setMessage(
                                      err instanceof Error ? err.message : 'End failed',
                                    );
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
                                  return entries.refetch();
                                })
                                .catch((err: unknown) => {
                                  setMessage(
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
          </TablePanel>
        )}
      </section>
    </>
  );
}
