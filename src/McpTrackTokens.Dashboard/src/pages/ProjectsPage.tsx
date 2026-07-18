import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  useDeleteProjectMutation,
  useProjectsQuery,
  useReportsSummaryQuery,
  useUpdateProjectMutation,
} from '../api/hooks';
import type { ProjectDto, UpdateProjectRequest } from '../api/types';
import { ErrorState, LoadingState, EmptyState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import {
  formatCurrency,
  formatDateTime,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
} from '../utils/format';
import { Page } from '../layout/AppLayout';

type EditDraft = {
  name: string;
  slug: string;
  clientName: string;
  billingCode: string;
  currency: string;
  repositoryPath: string;
  remoteUrl: string;
  isActive: boolean;
};

function toDraft(project: ProjectDto): EditDraft {
  return {
    name: project.name,
    slug: project.slug,
    clientName: project.clientName ?? '',
    billingCode: project.billingCode ?? '',
    currency: project.currency,
    repositoryPath: project.primaryRepositoryPath ?? '',
    remoteUrl: project.primaryRemoteUrl ?? '',
    isActive: project.isActive,
  };
}

export function ProjectsPage() {
  const now = new Date();
  const projects = useProjectsQuery();
  const summary = useReportsSummaryQuery(now.getUTCFullYear(), now.getUTCMonth() + 1);
  const updateMutation = useUpdateProjectMutation();
  const deleteMutation = useDeleteProjectMutation();

  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<EditDraft | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const list = [...(projects.data ?? [])].sort((a, b) => {
    const ta = a.lastActivityAtUtc ? Date.parse(a.lastActivityAtUtc) : 0;
    const tb = b.lastActivityAtUtc ? Date.parse(b.lastActivityAtUtc) : 0;
    if (tb !== ta) return tb - ta;
    return a.name.localeCompare(b.name);
  });
  const editing = list.find((p) => p.id === editingId) ?? null;

  useEffect(() => {
    if (editing) {
      setDraft(toDraft(editing));
    } else {
      setDraft(null);
    }
  }, [editing]);

  if (projects.isLoading) {
    return <LoadingState label="Loading projects…" />;
  }

  if (projects.error) {
    return (
      <ErrorState
        message={projects.error instanceof Error ? projects.error.message : 'Failed to load projects'}
        error={projects.error}
      />
    );
  }

  const costByProject = new Map(
    (summary.data?.projects ?? []).map((p) => [p.projectId, p] as const),
  );

  async function onSaveEdit(event: React.FormEvent) {
    event.preventDefault();
    if (!editingId || !draft) {
      return;
    }

    setActionMessage(null);
    const body: UpdateProjectRequest = {
      name: draft.name.trim(),
      slug: draft.slug.trim() || null,
      clientName: draft.clientName.trim() || null,
      billingCode: draft.billingCode.trim() || null,
      currency: draft.currency.trim() || null,
      repositoryPath: draft.repositoryPath.trim() || null,
      remoteUrl: draft.remoteUrl.trim() || null,
      isActive: draft.isActive,
    };

    try {
      await updateMutation.mutateAsync({ id: editingId, body });
      setActionMessage('Project updated.');
      setEditingId(null);
    } catch (err) {
      setActionMessage(err instanceof Error ? err.message : 'Update failed');
    }
  }

  async function onDelete(project: ProjectDto) {
    const ok = window.confirm(
      `Delete project “${project.name}”? It will be deactivated and hidden from the active list.`,
    );
    if (!ok) {
      return;
    }

    setActionMessage(null);
    try {
      await deleteMutation.mutateAsync(project.id);
      if (editingId === project.id) {
        setEditingId(null);
      }
      setActionMessage(`Deleted “${project.name}”.`);
    } catch (err) {
      setActionMessage(err instanceof Error ? err.message : 'Delete failed');
    }
  }

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>All projects</h2>
            <p>{formatNumber(list.length)} registered projects</p>
          </div>
        </div>

        {actionMessage ? <p className="form-message">{actionMessage}</p> : null}

        {list.length === 0 ? (
          <EmptyState message="No projects yet. Register one from the CLI, MCP tool, or editor extension." />
        ) : (
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Client</th>
                  <th>Repos</th>
                  <th>Prompts</th>
                  <th>Agent duration</th>
                  <th>Active time</th>
                  <th>Cost</th>
                  <th>Last activity</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {list.map((project) => {
                  const cost = costByProject.get(project.id);
                  return (
                    <tr key={project.id}>
                      <td>
                        <Link to={`/projects/${project.id}`}>{project.name}</Link>
                      </td>
                      <td>{project.clientName ?? '—'}</td>
                      <td>{formatNumber(project.repositoryCount)}</td>
                      <td>{formatNumber(cost?.promptCount ?? project.promptCount)}</td>
                      <td>
                        {formatDurationMs(
                          cost?.agentDurationMilliseconds ?? project.agentDurationMilliseconds,
                        )}
                      </td>
                      <td>
                        {formatDurationSeconds(
                          cost?.activeProjectTimeSeconds ?? project.activeProjectTimeSeconds,
                        )}
                      </td>
                      <td>
                        {formatCurrency(
                          cost?.totalAiCost ?? project.totalAiCost,
                          cost?.currency ?? project.currency,
                        )}
                      </td>
                      <td>{formatDateTime(project.lastActivityAtUtc)}</td>
                      <td>
                        <StatusBadge
                          label={project.isActive ? 'Active' : 'Inactive'}
                          tone={project.isActive ? 'success' : 'neutral'}
                        />
                      </td>
                      <td>
                        <div className="row-actions">
                          <button
                            type="button"
                            className="btn btn-secondary btn-compact"
                            onClick={() => {
                              setActionMessage(null);
                              setEditingId(project.id);
                            }}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="btn btn-danger btn-compact"
                            disabled={deleteMutation.isPending}
                            onClick={() => void onDelete(project)}
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
          </div>
        )}
      </section>

      {editing && draft ? (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Edit project</h2>
              <p>
                Updating <Link to={`/projects/${editing.id}`}>{editing.name}</Link>
              </p>
            </div>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => setEditingId(null)}
            >
              Cancel
            </button>
          </div>

          <form className="panel stack" onSubmit={(e) => void onSaveEdit(e)}>
            <div className="field-row">
              <div className="field">
                <label htmlFor="edit-name">Name</label>
                <input
                  id="edit-name"
                  required
                  value={draft.name}
                  onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))}
                />
              </div>
              <div className="field">
                <label htmlFor="edit-slug">Slug</label>
                <input
                  id="edit-slug"
                  value={draft.slug}
                  onChange={(e) => setDraft((d) => (d ? { ...d, slug: e.target.value } : d))}
                />
              </div>
              <div className="field">
                <label htmlFor="edit-client">Client</label>
                <input
                  id="edit-client"
                  value={draft.clientName}
                  onChange={(e) => setDraft((d) => (d ? { ...d, clientName: e.target.value } : d))}
                />
              </div>
              <div className="field">
                <label htmlFor="edit-billing">Billing code</label>
                <input
                  id="edit-billing"
                  value={draft.billingCode}
                  onChange={(e) => setDraft((d) => (d ? { ...d, billingCode: e.target.value } : d))}
                />
              </div>
            </div>

            <div className="field-row">
              <div className="field">
                <label htmlFor="edit-currency">Currency</label>
                <input
                  id="edit-currency"
                  value={draft.currency}
                  onChange={(e) => setDraft((d) => (d ? { ...d, currency: e.target.value } : d))}
                />
              </div>
              <div className="field">
                <label htmlFor="edit-repo">Repository path</label>
                <input
                  id="edit-repo"
                  value={draft.repositoryPath}
                  onChange={(e) =>
                    setDraft((d) => (d ? { ...d, repositoryPath: e.target.value } : d))
                  }
                />
              </div>
              <div className="field">
                <label htmlFor="edit-remote">Remote URL</label>
                <input
                  id="edit-remote"
                  value={draft.remoteUrl}
                  onChange={(e) => setDraft((d) => (d ? { ...d, remoteUrl: e.target.value } : d))}
                />
              </div>
              <div className="field">
                <label htmlFor="edit-active">
                  <input
                    id="edit-active"
                    type="checkbox"
                    checked={draft.isActive}
                    onChange={(e) =>
                      setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))
                    }
                  />{' '}
                  Active
                </label>
              </div>
            </div>

            <div className="row-actions">
              <button type="submit" className="btn" disabled={updateMutation.isPending}>
                {updateMutation.isPending ? 'Saving…' : 'Save changes'}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setEditingId(null)}
              >
                Cancel
              </button>
            </div>
          </form>
        </section>
      ) : null}
    </Page>
  );
}
