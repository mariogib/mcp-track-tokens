import { useMemo, useState } from 'react';
import {
  useAssignActivityMutation,
  useProjectsQuery,
  useUnallocatedQuery,
} from '../api/hooks';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { Panel, TablePanel } from '../components/MetricCard';
import {
  formatDateTime,
  formatDurationMs,
  formatNumber,
  lastDaysRange,
} from '../utils/format';

/** Unallocated activity assign UI (embedded under Imported usage tabs). */
export function UnallocatedActivityPanel() {
  const range = useMemo(() => lastDaysRange(30), []);
  const unallocated = useUnallocatedQuery(range.fromUtc, range.toUtc);
  const projects = useProjectsQuery();
  const assign = useAssignActivityMutation();

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [projectId, setProjectId] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  const items = unallocated.data?.activity ?? [];
  const activeProjects = (projects.data ?? []).filter((p) => p.isActive !== false);

  const toggle = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleAll = () => {
    if (selectedIds.size === items.length) {
      setSelectedIds(new Set());
      return;
    }
    setSelectedIds(new Set(items.map((i) => i.id)));
  };

  const onAssign = () => {
    if (!projectId || selectedIds.size === 0) return;
    setMessage(null);
    assign.mutate(
      { projectId, eventIds: [...selectedIds] },
      {
        onSuccess: (result) => {
          setSelectedIds(new Set());
          setMessage(`Assigned ${formatNumber(result.assigned)} event(s) to the selected project.`);
        },
        onError: (err) => {
          setMessage(err instanceof Error ? err.message : 'Assign failed');
        },
      },
    );
  };

  if (unallocated.isLoading) {
    return <LoadingState label="Loading unallocated activity…" />;
  }

  if (unallocated.error) {
    return (
      <ErrorState
        message={
          unallocated.error instanceof Error
            ? unallocated.error.message
            : 'Failed to load unallocated activity'
        }
      />
    );
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>Unallocated activity</h2>
          <p>Select events and assign them to a tracked project.</p>
        </div>
      </div>

      <Panel className="stack">
        <div className="field-row">
          <div className="field">
            <label htmlFor="activity-project">Project</label>
            <select
              id="activity-project"
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
            >
              <option value="">Select project…</option>
              {activeProjects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field" style={{ justifyContent: 'flex-end' }}>
            <label className="label">&nbsp;</label>
            <button
              type="button"
              className="btn"
              disabled={!projectId || selectedIds.size === 0 || assign.isPending}
              onClick={onAssign}
            >
              {assign.isPending
                ? 'Assigning…'
                : `Assign ${selectedIds.size || ''} selected`}
            </button>
          </div>
        </div>
        {message ? <p className="hint">{message}</p> : null}
      </Panel>

      {items.length === 0 ? (
        <EmptyState message="No unallocated activity in the last 30 days." />
      ) : (
        <TablePanel>
          <table className="data">
            <thead>
              <tr>
                <th>
                  <input
                    type="checkbox"
                    checked={items.length > 0 && selectedIds.size === items.length}
                    onChange={toggleAll}
                    aria-label="Select all"
                  />
                </th>
                <th>When</th>
                <th>Type</th>
                <th>Editor</th>
                <th>Model</th>
                <th>Workspace</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id} className={selectedIds.has(item.id) ? 'is-selected' : undefined}>
                  <td>
                    <input
                      type="checkbox"
                      checked={selectedIds.has(item.id)}
                      onChange={() => toggle(item.id)}
                      aria-label={`Select ${item.id}`}
                    />
                  </td>
                  <td>{formatDateTime(item.timestampUtc)}</td>
                  <td>{item.eventType ?? item.kind}</td>
                  <td>{item.editor ?? '—'}</td>
                  <td>{item.model ?? '—'}</td>
                  <td className="mono">{item.workspacePath ?? item.repositoryPath ?? '—'}</td>
                  <td>{formatDurationMs(item.durationMilliseconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TablePanel>
      )}
    </section>
  );
}
