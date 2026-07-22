import { useEffect, useMemo, useState, type MouseEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { BrowseListControls, TextLink } from '../shared/adminUi';
import { exportToExcel } from '@lunarq/frontend-shared/utils';
import type { BrowseViewMode } from '@lunarq/frontend-shared/components';
import {
  useDeleteProjectMutation,
  useProjectsQuery,
  useReportsSummaryQuery,
  useUpdateProjectMutation,
} from '../api/hooks';
import type { ProjectDto, UpdateProjectRequest } from '../api/types';
import { ErrorState, LoadingState, EmptyState } from '../components/States';
import { Panel, TablePanel } from '../components/MetricCard';
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
  const navigate = useNavigate();
  const now = new Date();
  const projects = useProjectsQuery();
  const summary = useReportsSummaryQuery(now.getUTCFullYear(), now.getUTCMonth() + 1);
  const updateMutation = useUpdateProjectMutation();
  const deleteMutation = useDeleteProjectMutation();

  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<EditDraft | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<BrowseViewMode>('grid');
  const [searchValue, setSearchValue] = useState('');
  const [clientFilter, setClientFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const list = useMemo(() => {
    return [...(projects.data ?? [])].sort((a, b) => {
      const ta = a.lastActivityAtUtc ? Date.parse(a.lastActivityAtUtc) : 0;
      const tb = b.lastActivityAtUtc ? Date.parse(b.lastActivityAtUtc) : 0;
      if (tb !== ta) return tb - ta;
      return a.name.localeCompare(b.name);
    });
  }, [projects.data]);

  const costByProject = useMemo(
    () => new Map((summary.data?.projects ?? []).map((p) => [p.projectId, p] as const)),
    [summary.data?.projects],
  );

  const clientOptions = useMemo(() => {
    const clients = new Set<string>();
    let hasUnassigned = false;
    for (const project of list) {
      const client = project.clientName?.trim();
      if (client) {
        clients.add(client);
      } else {
        hasUnassigned = true;
      }
    }
    return {
      clients: [...clients].sort((a, b) => a.localeCompare(b)),
      hasUnassigned,
    };
  }, [list]);

  const filteredList = useMemo(() => {
    const query = searchValue.trim().toLowerCase();

    return list.filter((project) => {
      const clientName = project.clientName?.trim() ?? '';
      const matchesClient =
        !clientFilter ||
        (clientFilter === '__none__' ? !clientName : clientName === clientFilter);
      const matchesStatus =
        !statusFilter ||
        (statusFilter === 'active' ? project.isActive : !project.isActive);
      if (!matchesClient || !matchesStatus) {
        return false;
      }

      if (!query) {
        return true;
      }

      const haystack = [
        project.name,
        project.slug,
        clientName,
        project.billingCode ?? '',
        project.isActive ? 'active' : 'inactive',
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(query);
    });
  }, [clientFilter, list, searchValue, statusFilter]);

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

  async function onExportToExcel() {
    const timestamp = new Date().toISOString();
    await exportToExcel({
      filename: 'projects.xlsx',
      title: 'Projects',
      timestamp,
      columns: [
        { header: 'Name', key: 'name' },
        { header: 'Client', key: 'clientName' },
        { header: 'Slug', key: 'slug' },
        { header: 'Repos', key: 'repositoryCount' },
        { header: 'Prompts', key: 'promptCount' },
        { header: 'Agent duration (ms)', key: 'agentDurationMilliseconds' },
        { header: 'Active time (s)', key: 'activeProjectTimeSeconds' },
        { header: 'Calculated cost', key: 'calculatedTokenCost' },
        { header: 'Cost', key: 'totalAiCost' },
        { header: 'Currency', key: 'currency' },
        { header: 'Last activity', key: 'lastActivityAtUtc' },
        { header: 'Status', key: 'status' },
      ],
      data: filteredList.map((project) => {
        const cost = costByProject.get(project.id);
        return {
          name: project.name,
          clientName: project.clientName ?? '',
          slug: project.slug,
          repositoryCount: project.repositoryCount,
          promptCount: cost?.promptCount ?? project.promptCount,
          agentDurationMilliseconds:
            cost?.agentDurationMilliseconds ?? project.agentDurationMilliseconds,
          activeProjectTimeSeconds:
            cost?.activeProjectTimeSeconds ?? project.activeProjectTimeSeconds,
          calculatedTokenCost: cost?.calculatedTokenCost ?? 0,
          totalAiCost: cost?.totalAiCost ?? project.totalAiCost,
          currency: cost?.currency ?? project.currency,
          lastActivityAtUtc: project.lastActivityAtUtc ?? '',
          status: project.isActive ? 'Active' : 'Inactive',
        };
      }),
    });
  }

  function renderProjectActions(project: ProjectDto) {
    return (
      <div
        className="row-actions"
        onClick={(event) => event.stopPropagation()}
        onKeyDown={(event) => event.stopPropagation()}
      >
        <button
          type="button"
          className="btn btn-secondary btn-compact"
          onClick={() => navigate(`/projects/${project.id}`)}
        >
          Open
        </button>
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
    );
  }

  return (
    <Page>
      <section className="page-section">
        <BrowseListControls
          heading="All projects"
          viewMode={viewMode === 'calendar' ? 'table' : viewMode}
          onViewModeChange={(next) => {
            if (next === 'calendar') {
              setViewMode('table');
              return;
            }
            setViewMode(next);
          }}
          allowCalendarView={false}
          searchValue={searchValue}
          searchPlaceholder="Search projects..."
          onSearchChange={setSearchValue}
          onExportToExcel={() => void onExportToExcel()}
          exportLabel="Export to Excel"
          exportDisabled={filteredList.length === 0}
          filters={[
            {
              id: 'projects-client-filter',
              label: 'Client',
              value: clientFilter,
              onChange: setClientFilter,
              options: [
                { value: '', label: 'All clients' },
                ...clientOptions.clients.map((client) => ({
                  value: client,
                  label: client,
                })),
                ...(clientOptions.hasUnassigned
                  ? [{ value: '__none__', label: 'No client' }]
                  : []),
              ],
            },
            {
              id: 'projects-status-filter',
              label: 'Status',
              value: statusFilter,
              onChange: setStatusFilter,
              options: [
                { value: '', label: 'All statuses' },
                { value: 'active', label: 'Active' },
                { value: 'inactive', label: 'Inactive' },
              ],
            },
          ]}
        />

        <p className="section-meta">
          Showing {formatNumber(filteredList.length)} of {formatNumber(list.length)} registered projects
          {clientFilter || statusFilter
            ? ` · filters: client=${clientFilter === '__none__' ? 'none' : clientFilter || 'all'}, status=${statusFilter || 'all'}`
            : ''}
        </p>

        {actionMessage ? <p className="form-message">{actionMessage}</p> : null}

        {list.length === 0 ? (
          <EmptyState message="No projects yet. Register one from the CLI, MCP tool, or editor extension." />
        ) : filteredList.length === 0 ? (
          <EmptyState message="No projects match the current search or filters." />
        ) : viewMode === 'grid' ? (
          <div className="projects-browse-grid">
            {filteredList.map((project) => {
              const cost = costByProject.get(project.id);
              const detailPath = `/projects/${project.id}`;
              return (
                <article
                  key={project.id}
                  className="projects-browse-tile projects-browse-tile-interactive"
                  role="link"
                  tabIndex={0}
                  aria-label={`Open project ${project.name}`}
                  onClick={() => navigate(detailPath)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      navigate(detailPath);
                    }
                  }}
                >
                  <div className="projects-browse-tile-header">
                    <TextLink
                      to={detailPath}
                      onClick={(event) => (event as MouseEvent).stopPropagation()}
                    >
                      {project.name}
                    </TextLink>
                    <StatusBadge
                      label={project.isActive ? 'Active' : 'Inactive'}
                      tone={project.isActive ? 'success' : 'neutral'}
                    />
                  </div>
                  <p>{project.clientName ?? 'No client'}</p>
                  <dl className="projects-browse-tile-stats">
                    <div>
                      <dt>Prompts</dt>
                      <dd>{formatNumber(cost?.promptCount ?? project.promptCount)}</dd>
                    </div>
                    <div>
                      <dt>Active time</dt>
                      <dd>
                        {formatDurationSeconds(
                          cost?.activeProjectTimeSeconds ?? project.activeProjectTimeSeconds,
                        )}
                      </dd>
                    </div>
                    <div>
                      <dt>Calculated</dt>
                      <dd>
                        {formatCurrency(
                          cost?.calculatedTokenCost ?? 0,
                          cost?.currency ?? project.currency,
                        )}
                      </dd>
                    </div>
                    <div>
                      <dt>Cost</dt>
                      <dd>
                        {formatCurrency(
                          cost?.totalAiCost ?? project.totalAiCost,
                          cost?.currency ?? project.currency,
                        )}
                      </dd>
                    </div>
                  </dl>
                  <p className="projects-browse-tile-meta">
                    Last activity {formatDateTime(project.lastActivityAtUtc)}
                  </p>
                  {renderProjectActions(project)}
                </article>
              );
            })}
          </div>
        ) : (
          <TablePanel>
            <table className="data">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Client</th>
                  <th>Repos</th>
                  <th>Prompts</th>
                  <th>Agent duration</th>
                  <th>Active time</th>
                  <th>Calculated cost</th>
                  <th>Cost</th>
                  <th>Last activity</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredList.map((project) => {
                  const cost = costByProject.get(project.id);
                  const currency = cost?.currency ?? project.currency;
                  return (
                    <tr key={project.id}>
                      <td>
                        <TextLink to={`/projects/${project.id}`}>{project.name}</TextLink>
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
                      <td>{formatCurrency(cost?.calculatedTokenCost ?? 0, currency)}</td>
                      <td>
                        {formatCurrency(cost?.totalAiCost ?? project.totalAiCost, currency)}
                      </td>
                      <td>{formatDateTime(project.lastActivityAtUtc)}</td>
                      <td>
                        <StatusBadge
                          label={project.isActive ? 'Active' : 'Inactive'}
                          tone={project.isActive ? 'success' : 'neutral'}
                        />
                      </td>
                      <td>{renderProjectActions(project)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </TablePanel>
        )}
      </section>

      {editing && draft ? (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Edit project</h2>
              <p>
                Updating <TextLink to={`/projects/${editing.id}`}>{editing.name}</TextLink>
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

          <Panel className="stack"><form className="stack" onSubmit={(e) => void onSaveEdit(e)}>
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
          </form></Panel>
        </section>
      ) : null}
    </Page>
  );
}
