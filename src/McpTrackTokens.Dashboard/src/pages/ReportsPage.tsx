import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  useClientCostQuery,
  useClientTokenCostQuery,
  useEditorComparisonReportQuery,
  useModelCostReportQuery,
  useProjectActivityQuery,
  useProjectCostQuery,
  useProjectTokenCostQuery,
  useProjectsQuery,
  useReportClientsQuery,
  useReportsSummaryQuery,
} from '../api/hooks';
import {
  ChartCard,
  DailyLineChart,
  NamedBarChart,
  NamedPieChart,
} from '../components/Charts';
import { MetricCard } from '../components/MetricCard';
import { EmptyState, ErrorState, LoadingState } from '../components/States';
import { Page } from '../layout/AppLayout';
import {
  formatCurrency,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
  lastDaysRange,
  monthBoundsUtc,
} from '../utils/format';

const SECTIONS = ['Clients', 'Projects'] as const;
type Section = (typeof SECTIONS)[number];

type RangePreset = '7d' | '30d' | '90d' | 'month';

type ClientReportId =
  | 'client-billing'
  | 'client-token-cost'
  | 'clients-overview'
  | 'model-cost';
type ProjectReportId =
  | 'project-cost'
  | 'project-activity'
  | 'project-token-cost'
  | 'projects-monthly'
  | 'editor-comparison';

const CLIENT_REPORTS: { id: ClientReportId; title: string; description: string }[] = [
  {
    id: 'client-billing',
    title: 'Client billing summary',
    description: 'Roll up AI cost, prompts, and time across every project for one client.',
  },
  {
    id: 'client-token-cost',
    title: 'Client token cost (rate card)',
    description:
      'Calculated cost = Settings Cursor token rates × attributed tokens used, rolled up for one client.',
  },
  {
    id: 'clients-overview',
    title: 'Clients cost overview',
    description: 'Compare total AI cost by client for the selected month.',
  },
  {
    id: 'model-cost',
    title: 'Model cost breakdown',
    description: 'Usage-based cost by model across all projects in the date range.',
  },
];

const PROJECT_REPORTS: { id: ProjectReportId; title: string; description: string }[] = [
  {
    id: 'project-cost',
    title: 'Project cost',
    description: 'Usage, subscription allocation, and total AI cost for one project.',
  },
  {
    id: 'project-activity',
    title: 'Project activity',
    description: 'Prompts, agent time, and daily activity for one project.',
  },
  {
    id: 'project-token-cost',
    title: 'Project token cost (rate card)',
    description:
      'Calculated cost = Settings Cursor token rates × attributed tokens used for one project.',
  },
  {
    id: 'projects-monthly',
    title: 'Projects monthly rollup',
    description: 'All project costs for the selected calendar month.',
  },
  {
    id: 'editor-comparison',
    title: 'Editor comparison',
    description: 'Compare prompt and agent activity across editors.',
  },
];

function resolveRange(preset: RangePreset): { fromUtc: string; toUtc: string; label: string } {
  const now = new Date();
  if (preset === 'month') {
    const year = now.getUTCFullYear();
    const month = now.getUTCMonth() + 1;
    const bounds = monthBoundsUtc(year, month);
    return {
      ...bounds,
      label: `${year}-${String(month).padStart(2, '0')}`,
    };
  }

  const days = preset === '7d' ? 7 : preset === '90d' ? 90 : 30;
  return { ...lastDaysRange(days), label: `Last ${days} days` };
}

export function ReportsPage() {
  const [section, setSection] = useState<Section>('Clients');
  const [clientReport, setClientReport] = useState<ClientReportId>('client-billing');
  const [projectReport, setProjectReport] = useState<ProjectReportId>('project-cost');
  const [rangePreset, setRangePreset] = useState<RangePreset>('30d');
  const [clientName, setClientName] = useState('');
  const [projectId, setProjectId] = useState('');

  const range = useMemo(() => resolveRange(rangePreset), [rangePreset]);
  const now = new Date();
  const year = now.getUTCFullYear();
  const month = now.getUTCMonth() + 1;

  const clients = useReportClientsQuery();
  const projects = useProjectsQuery();
  const summary = useReportsSummaryQuery(year, month);

  const sortedClients = useMemo(() => {
    const rows = [...(clients.data ?? [])];
    rows.sort((a, b) => {
      if (b.projectCount !== a.projectCount) return b.projectCount - a.projectCount;
      return a.name.localeCompare(b.name);
    });
    return rows;
  }, [clients.data]);

  const selectedClient = clientName || sortedClients[0]?.name || '';
  const selectedProject =
    projectId ||
    projects.data?.[0]?.id ||
    '';

  const clientBilling = useClientCostQuery(
    selectedClient,
    range.fromUtc,
    range.toUtc,
    section === 'Clients' && clientReport === 'client-billing',
  );
  const clientTokenCost = useClientTokenCostQuery(
    selectedClient,
    range.fromUtc,
    range.toUtc,
    section === 'Clients' && clientReport === 'client-token-cost',
  );
  const modelCost = useModelCostReportQuery(
    range.fromUtc,
    range.toUtc,
    section === 'Clients' && clientReport === 'model-cost',
  );
  const projectCost = useProjectCostQuery(
    selectedProject,
    range.fromUtc,
    range.toUtc,
  );
  const projectActivity = useProjectActivityQuery(
    selectedProject,
    range.fromUtc,
    range.toUtc,
  );
  const projectTokenCost = useProjectTokenCostQuery(
    selectedProject,
    range.fromUtc,
    range.toUtc,
  );
  const editors = useEditorComparisonReportQuery(
    range.fromUtc,
    range.toUtc,
    section === 'Projects' && projectReport === 'editor-comparison',
  );

  // Only enable project detail queries when needed
  const showProjectCost =
    section === 'Projects' && projectReport === 'project-cost' && Boolean(selectedProject);
  const showProjectActivity =
    section === 'Projects' && projectReport === 'project-activity' && Boolean(selectedProject);
  const showProjectToken =
    section === 'Projects' && projectReport === 'project-token-cost' && Boolean(selectedProject);

  const clientsOverview = useMemo(() => {
    const rows = summary.data?.projects ?? [];
    const map = new Map<
      string,
      { clientName: string; projectCount: number; totalAiCost: number; promptCount: number }
    >();
    for (const row of rows) {
      const key = row.clientName?.trim() || 'Unassigned';
      const existing = map.get(key) ?? {
        clientName: key,
        projectCount: 0,
        totalAiCost: 0,
        promptCount: 0,
      };
      existing.projectCount += 1;
      existing.totalAiCost += row.totalAiCost ?? 0;
      existing.promptCount += row.promptCount ?? 0;
      map.set(key, existing);
    }
    return [...map.values()].sort((a, b) => b.totalAiCost - a.totalAiCost);
  }, [summary.data?.projects]);

  const reportCards = section === 'Clients' ? CLIENT_REPORTS : PROJECT_REPORTS;
  const activeReportId = section === 'Clients' ? clientReport : projectReport;

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Reports</h2>
            <p>Default client and project reports backed by tracked activity and imported usage.</p>
          </div>
        </div>

        <div className="tabs" role="tablist" aria-label="Report sections">
          {SECTIONS.map((name) => (
            <button
              key={name}
              type="button"
              role="tab"
              aria-selected={section === name}
              className={`tab${section === name ? ' active' : ''}`}
              onClick={() => setSection(name)}
            >
              {name}
            </button>
          ))}
        </div>
      </section>

      <section className="page-section">
        <div className="section-header">
          <div>
            <h3>Default reports</h3>
            <p className="muted">Choose a report, then adjust the filters below.</p>
          </div>
        </div>
        <div className="metric-grid">
          {reportCards.map((report) => {
            const selected = report.id === activeReportId;
            return (
              <button
                key={report.id}
                type="button"
                className={`panel stack report-card${selected ? ' report-card--active' : ''}`}
                onClick={() => {
                  if (section === 'Clients') {
                    setClientReport(report.id as ClientReportId);
                  } else {
                    setProjectReport(report.id as ProjectReportId);
                  }
                }}
                style={{
                  textAlign: 'left',
                  cursor: 'pointer',
                  borderColor: selected ? 'var(--accent)' : undefined,
                }}
              >
                <strong>{report.title}</strong>
                <span className="muted">{report.description}</span>
              </button>
            );
          })}
        </div>
      </section>

      <section className="page-section">
        <div className="panel stack">
          <div className="field-row">
            <div className="field">
              <label htmlFor="report-range">Date range</label>
              <select
                id="report-range"
                value={rangePreset}
                onChange={(e) => setRangePreset(e.target.value as RangePreset)}
              >
                <option value="7d">Last 7 days</option>
                <option value="30d">Last 30 days</option>
                <option value="90d">Last 90 days</option>
                <option value="month">This month</option>
              </select>
            </div>

            {section === 'Clients' &&
            (clientReport === 'client-billing' || clientReport === 'client-token-cost') ? (
              <div className="field">
                <label htmlFor="report-client">Client</label>
                <select
                  id="report-client"
                  value={selectedClient}
                  onChange={(e) => setClientName(e.target.value)}
                  disabled={!clients.data?.length}
                >
                  {sortedClients.length === 0 ? (
                    <option value="">No clients assigned</option>
                  ) : (
                    sortedClients.map((client) => (
                      <option key={client.name} value={client.name}>
                        {client.name} ({client.projectCount})
                      </option>
                    ))
                  )}
                </select>
              </div>
            ) : null}

            {section === 'Projects' &&
            (projectReport === 'project-cost' ||
              projectReport === 'project-activity' ||
              projectReport === 'project-token-cost') ? (
              <div className="field">
                <label htmlFor="report-project">Project</label>
                <select
                  id="report-project"
                  value={selectedProject}
                  onChange={(e) => setProjectId(e.target.value)}
                  disabled={!projects.data?.length}
                >
                  {(projects.data ?? []).length === 0 ? (
                    <option value="">No projects</option>
                  ) : (
                    projects.data!.map((project) => (
                      <option key={project.id} value={project.id}>
                        {project.name}
                        {project.clientName ? ` · ${project.clientName}` : ''}
                      </option>
                    ))
                  )}
                </select>
              </div>
            ) : null}
          </div>
          <p className="muted">
            Range: {range.label} ({new Date(range.fromUtc).toLocaleString()} –{' '}
            {new Date(range.toUtc).toLocaleString()})
          </p>
        </div>
      </section>

      {section === 'Clients' && clientReport === 'client-billing' && (
        <ClientBillingReport
          query={clientBilling}
          hasClients={Boolean(clients.data?.length)}
        />
      )}

      {section === 'Clients' && clientReport === 'client-token-cost' && (
        <ClientTokenCostReportView
          query={clientTokenCost}
          hasClients={Boolean(clients.data?.length)}
        />
      )}

      {section === 'Clients' && clientReport === 'clients-overview' && (
        <ClientsOverviewReport
          rows={clientsOverview}
          currency={summary.data?.currency ?? 'USD'}
          loading={summary.isLoading}
          error={summary.error}
          year={year}
          month={month}
        />
      )}

      {section === 'Clients' && clientReport === 'model-cost' && (
        <ModelCostReportView query={modelCost} />
      )}

      {section === 'Projects' && projectReport === 'project-cost' && showProjectCost && (
        <ProjectCostReportView query={projectCost} projectId={selectedProject} />
      )}

      {section === 'Projects' && projectReport === 'project-activity' && showProjectActivity && (
        <ProjectActivityReportView query={projectActivity} projectId={selectedProject} />
      )}

      {section === 'Projects' && projectReport === 'project-token-cost' && showProjectToken && (
        <ProjectTokenCostReportView query={projectTokenCost} projectId={selectedProject} />
      )}

      {section === 'Projects' && projectReport === 'projects-monthly' && (
        <ProjectsMonthlyReport
          summary={summary}
          year={year}
          month={month}
        />
      )}

      {section === 'Projects' && projectReport === 'editor-comparison' && (
        <EditorComparisonReportView query={editors} />
      )}
    </Page>
  );
}

function ClientBillingReport({
  query,
  hasClients,
}: {
  query: ReturnType<typeof useClientCostQuery>;
  hasClients: boolean;
}) {
  if (!hasClients) {
    return (
      <EmptyState message="Assign a client name on projects to run client billing reports." />
    );
  }
  if (query.isLoading) return <LoadingState label="Loading client billing…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const data = query.data;
  if (!data) return <EmptyState message="No client billing data for this range." />;

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>{data.clientName}</h3>
          <p className="muted">
            {data.projectCount} project{data.projectCount === 1 ? '' : 's'} in range
          </p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard
          label="Total AI cost"
          value={formatCurrency(data.totalAiCost, data.currency)}
          hint={`Usage ${formatCurrency(data.usageBasedCost, data.currency)} · Sub ${formatCurrency(data.subscriptionAllocation, data.currency)}`}
        />
        <MetricCard label="Prompts" value={formatNumber(data.promptCount)} />
        <MetricCard
          label="Agent time"
          value={formatDurationMs(data.agentDurationMilliseconds)}
        />
        <MetricCard
          label="Active project time"
          value={formatDurationSeconds(data.activeProjectTimeSeconds)}
        />
      </div>
      {data.projects.length === 0 ? (
        <EmptyState message="No project activity for this client in the selected range." />
      ) : (
        <>
          <div className="chart-grid">
            <ChartCard title="Cost by project">
              <NamedBarChart
                data={data.projects.map((p) => ({
                  name: p.projectName,
                  value: p.totalAiCost,
                }))}
                valueKey="value"
                valueLabel="Cost"
              />
            </ChartCard>
            <ChartCard title="Cost share">
              <NamedPieChart
                data={data.projects.map((p) => ({
                  name: p.projectName,
                  value: p.totalAiCost,
                }))}
                valueKey="value"
              />
            </ChartCard>
          </div>
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Project</th>
                  <th>Prompts</th>
                  <th>Active time</th>
                  <th>Usage</th>
                  <th>Subscription</th>
                  <th>Total</th>
                </tr>
              </thead>
              <tbody>
                {data.projects.map((project) => (
                  <tr key={project.projectId}>
                    <td>
                      <Link to={`/projects/${project.projectId}`}>{project.projectName}</Link>
                    </td>
                    <td>{formatNumber(project.promptCount)}</td>
                    <td>{formatDurationSeconds(project.activeProjectTimeSeconds)}</td>
                    <td>{formatCurrency(project.usageBasedCursorCost, project.currency)}</td>
                    <td>{formatCurrency(project.subscriptionAllocation, project.currency)}</td>
                    <td>{formatCurrency(project.totalAiCost, project.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}

function ClientTokenCostReportView({
  query,
  hasClients,
}: {
  query: ReturnType<typeof useClientTokenCostQuery>;
  hasClients: boolean;
}) {
  if (!hasClients) {
    return (
      <EmptyState message="Assign a client name on projects to run client token cost reports." />
    );
  }
  if (query.isLoading) return <LoadingState label="Loading client token cost…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const data = query.data;
  if (!data) return <EmptyState message="No token usage for this client in range." />;

  if (data.totalTokens === 0) {
    return (
      <EmptyState message="No attributed token usage for this client in the selected range. Pick another client, widen the date range, or run usage reconciliation so imports are assigned to projects." />
    );
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>{data.clientName}</h3>
          <p className="muted">
            Calculated from Settings → Cursor token costs × attributed tokens
            {data.hasRateCard
              ? ` (${data.rateCardModelCount} rate card models).`
              : ' (no rate card configured).'}
          </p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard
          label="Calculated token cost"
          value={formatCurrency(data.estimatedCost, data.currency)}
          hint={`Reported import cost ${formatCurrency(data.reportedCost, data.currency)}`}
        />
        <MetricCard label="Total tokens" value={formatNumber(data.totalTokens)} />
        <MetricCard label="Input tokens" value={formatNumber(data.inputTokens)} />
        <MetricCard label="Output tokens" value={formatNumber(data.outputTokens)} />
        <MetricCard label="Cached tokens" value={formatNumber(data.cachedInputTokens)} />
        <MetricCard label="Projects" value={formatNumber(data.projectCount)} />
      </div>
      {data.projects.length === 0 ? (
        <EmptyState message="No attributed token usage for this client in the selected range." />
      ) : (
        <>
          <div className="chart-grid">
            <ChartCard title="Calculated cost by project">
              <NamedBarChart
                data={data.projects.map((p) => ({
                  name: p.projectName,
                  value: p.estimatedCost,
                }))}
                valueKey="value"
                valueLabel="Cost"
              />
            </ChartCard>
            {data.byModel.length > 0 ? (
              <ChartCard title="Calculated cost by model">
                <NamedBarChart
                  data={data.byModel.map((m) => ({
                    name: m.model,
                    value: m.estimatedCost,
                  }))}
                  valueKey="value"
                  valueLabel="Cost"
                />
              </ChartCard>
            ) : null}
          </div>
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Project</th>
                  <th>Tokens</th>
                  <th>Calculated cost</th>
                  <th>Reported cost</th>
                </tr>
              </thead>
              <tbody>
                {data.projects.map((project) => (
                  <tr key={project.projectId}>
                    <td>
                      <Link to={`/projects/${project.projectId}`}>{project.projectName}</Link>
                    </td>
                    <td>{formatNumber(project.totalTokens)}</td>
                    <td>{formatCurrency(project.estimatedCost, project.currency)}</td>
                    <td>{formatCurrency(project.reportedCost, project.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {data.byModel.length > 0 ? (
            <div className="table-wrap" style={{ marginTop: '1rem' }}>
              <table className="data">
                <thead>
                  <tr>
                    <th>Model</th>
                    <th>Rate source</th>
                    <th>Tokens</th>
                    <th>In / M</th>
                    <th>Out / M</th>
                    <th>Calculated</th>
                  </tr>
                </thead>
                <tbody>
                  {data.byModel.map((row) => (
                    <tr key={row.model}>
                      <td>{row.model}</td>
                      <td>{row.rateSource}</td>
                      <td>{formatNumber(row.totalTokens)}</td>
                      <td>{formatCurrency(row.inputPerMillion, data.currency)}</td>
                      <td>{formatCurrency(row.outputPerMillion, data.currency)}</td>
                      <td>{formatCurrency(row.estimatedCost, data.currency)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}

function ClientsOverviewReport({
  rows,
  currency,
  loading,
  error,
  year,
  month,
}: {
  rows: { clientName: string; projectCount: number; totalAiCost: number; promptCount: number }[];
  currency: string;
  loading: boolean;
  error: unknown;
  year: number;
  month: number;
}) {
  if (loading) return <LoadingState label="Loading clients overview…" />;
  if (error) {
    return (
      <ErrorState
        message={error instanceof Error ? error.message : 'Failed to load overview'}
      />
    );
  }
  if (rows.length === 0) {
    return <EmptyState message="No client cost data for this month." />;
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>Clients · {year}-{String(month).padStart(2, '0')}</h3>
          <p className="muted">Aggregated from the monthly project cost summary.</p>
        </div>
      </div>
      <div className="chart-grid">
        <ChartCard title="Cost by client">
          <NamedBarChart
            data={rows.map((r) => ({ name: r.clientName, value: r.totalAiCost }))}
            valueKey="value"
            valueLabel="Cost"
          />
        </ChartCard>
      </div>
      <div className="table-wrap">
        <table className="data">
          <thead>
            <tr>
              <th>Client</th>
              <th>Projects</th>
              <th>Prompts</th>
              <th>Total AI cost</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.clientName}>
                <td>{row.clientName}</td>
                <td>{formatNumber(row.projectCount)}</td>
                <td>{formatNumber(row.promptCount)}</td>
                <td>{formatCurrency(row.totalAiCost, currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function ModelCostReportView({
  query,
}: {
  query: ReturnType<typeof useModelCostReportQuery>;
}) {
  if (query.isLoading) return <LoadingState label="Loading model cost…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const models = query.data?.models ?? [];
  if (models.length === 0) {
    return <EmptyState message="No model cost data for this range." />;
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>Model cost</h3>
          <p className="muted">Usage-based and allocated cost by model.</p>
        </div>
      </div>
      <div className="chart-grid">
        <ChartCard title="Usage cost by model">
          <NamedBarChart
            data={models.map((m) => ({ name: m.model, value: m.usageBasedCost }))}
            valueKey="value"
            valueLabel="Cost"
          />
        </ChartCard>
      </div>
      <div className="table-wrap">
        <table className="data">
          <thead>
            <tr>
              <th>Model</th>
              <th>Provider</th>
              <th>Requests</th>
              <th>Tokens</th>
              <th>Usage cost</th>
              <th>Allocated</th>
            </tr>
          </thead>
          <tbody>
            {models.map((model) => (
              <tr key={`${model.model}-${model.provider ?? ''}`}>
                <td>{model.model}</td>
                <td>{model.provider ?? '—'}</td>
                <td>{formatNumber(model.requestCount)}</td>
                <td>{formatNumber(model.totalTokens)}</td>
                <td>
                  {formatCurrency(model.usageBasedCost, query.data?.currency ?? 'USD')}
                </td>
                <td>
                  {formatCurrency(model.allocatedCost, query.data?.currency ?? 'USD')}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function ProjectCostReportView({
  query,
  projectId,
}: {
  query: ReturnType<typeof useProjectCostQuery>;
  projectId: string;
}) {
  if (!projectId) return <EmptyState message="Select a project." />;
  if (query.isLoading) return <LoadingState label="Loading project cost…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const data = query.data;
  if (!data) return <EmptyState message="No cost data for this project." />;

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>
            <Link to={`/projects/${data.projectId}`}>{data.projectName}</Link>
          </h3>
          <p className="muted">{data.clientName ? `Client: ${data.clientName}` : 'No client assigned'}</p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard
          label="Total AI cost"
          value={formatCurrency(data.totalAiCost, data.currency)}
        />
        <MetricCard
          label="Usage cost"
          value={formatCurrency(data.usageBasedCursorCost, data.currency)}
        />
        <MetricCard
          label="Subscription"
          value={formatCurrency(data.subscriptionAllocation, data.currency)}
        />
        <MetricCard label="Prompts" value={formatNumber(data.promptCount)} />
        <MetricCard
          label="Active time"
          value={formatDurationSeconds(data.activeProjectTimeSeconds)}
        />
        <MetricCard label="Imported tokens" value={formatNumber(data.importedTotalTokens)} />
      </div>
      {data.byModel.length > 0 ? (
        <div className="chart-grid">
          <ChartCard title="Cost by model">
            <NamedBarChart
              data={data.byModel.map((m) => ({
                name: m.name,
                value: m.usageBasedCost + m.subscriptionAllocation,
              }))}
              valueKey="value"
              valueLabel="Cost"
            />
          </ChartCard>
        </div>
      ) : null}
    </section>
  );
}

function ProjectActivityReportView({
  query,
  projectId,
}: {
  query: ReturnType<typeof useProjectActivityQuery>;
  projectId: string;
}) {
  if (!projectId) return <EmptyState message="Select a project." />;
  if (query.isLoading) return <LoadingState label="Loading project activity…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const data = query.data;
  if (!data) return <EmptyState message="No activity for this project." />;

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>
            <Link to={`/projects/${data.projectId}`}>{data.projectName}</Link>
          </h3>
          <p className="muted">Activity for the selected range.</p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard label="Prompts" value={formatNumber(data.promptCount)} />
        <MetricCard label="Agent runs" value={formatNumber(data.agentRuns)} />
        <MetricCard
          label="Agent time"
          value={formatDurationMs(data.agentDurationMilliseconds)}
        />
        <MetricCard
          label="Active project time"
          value={formatDurationSeconds(data.activeProjectTimeSeconds)}
        />
        <MetricCard label="Sessions" value={formatNumber(data.sessionCount)} />
      </div>
      <div className="chart-grid">
        {data.byDay.length > 0 ? (
          <ChartCard title="Prompts by day">
            <DailyLineChart
              data={data.byDay.map((d) => ({
                day: String(d.day),
                prompts: d.promptCount,
              }))}
              xKey="day"
              yKey="prompts"
              yLabel="Prompts"
            />
          </ChartCard>
        ) : null}
        {data.byEditor.length > 0 ? (
          <ChartCard title="By editor">
            <NamedPieChart
              data={data.byEditor.map((e) => ({
                name: e.name,
                value: e.promptCount,
              }))}
              nameKey="name"
              valueKey="value"
            />
          </ChartCard>
        ) : null}
      </div>
    </section>
  );
}

function ProjectTokenCostReportView({
  query,
  projectId,
}: {
  query: ReturnType<typeof useProjectTokenCostQuery>;
  projectId: string;
}) {
  if (!projectId) return <EmptyState message="Select a project." />;
  if (query.isLoading) return <LoadingState label="Loading token cost…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const data = query.data;
  if (!data) return <EmptyState message="No token usage for this project in range." />;

  if (data.totalTokens === 0) {
    return (
      <EmptyState message="No attributed token usage for this project in the selected range. Widen the date range or run usage reconciliation so imports are assigned to this project." />
    );
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>
            <Link to={`/projects/${data.projectId}`}>{data.projectName}</Link>
          </h3>
          <p className="muted">
            Calculated from Settings → Cursor token costs × attributed tokens
            {data.hasRateCard
              ? ` (${data.rateCardModelCount} rate card models).`
              : ' (no rate card configured).'}
          </p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard
          label="Calculated token cost"
          value={formatCurrency(data.estimatedCost, data.currency)}
          hint={`Reported import cost ${formatCurrency(data.reportedCost, data.currency)}`}
        />
        <MetricCard label="Total tokens" value={formatNumber(data.totalTokens)} />
        <MetricCard label="Input tokens" value={formatNumber(data.inputTokens)} />
        <MetricCard label="Output tokens" value={formatNumber(data.outputTokens)} />
        <MetricCard label="Cached tokens" value={formatNumber(data.cachedInputTokens)} />
      </div>
      {data.byModel.length > 0 ? (
        <>
          <div className="chart-grid">
            <ChartCard title="Calculated cost by model">
              <NamedBarChart
                data={data.byModel.map((m) => ({
                  name: m.model,
                  value: m.estimatedCost,
                }))}
                valueKey="value"
                valueLabel="Cost"
              />
            </ChartCard>
          </div>
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Model</th>
                  <th>Rate source</th>
                  <th>Tokens</th>
                  <th>In / M</th>
                  <th>Out / M</th>
                  <th>Calculated</th>
                  <th>Reported</th>
                </tr>
              </thead>
              <tbody>
                {data.byModel.map((row) => (
                  <tr key={row.model}>
                    <td>{row.model}</td>
                    <td>{row.rateSource}</td>
                    <td>{formatNumber(row.totalTokens)}</td>
                    <td>{formatCurrency(row.inputPerMillion, data.currency)}</td>
                    <td>{formatCurrency(row.outputPerMillion, data.currency)}</td>
                    <td>{formatCurrency(row.estimatedCost, data.currency)}</td>
                    <td>{formatCurrency(row.reportedCost, data.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : (
        <EmptyState message="No attributed token usage for this project in the selected range." />
      )}
    </section>
  );
}

function ProjectsMonthlyReport({
  summary,
  year,
  month,
}: {
  summary: ReturnType<typeof useReportsSummaryQuery>;
  year: number;
  month: number;
}) {
  if (summary.isLoading) return <LoadingState label="Loading monthly rollup…" />;
  if (summary.error) {
    return (
      <ErrorState
        message={
          summary.error instanceof Error ? summary.error.message : 'Failed to load monthly report'
        }
      />
    );
  }
  const data = summary.data;
  if (!data) return <EmptyState message="No monthly summary available." />;

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>
            Projects · {year}-{String(month).padStart(2, '0')}
          </h3>
          <p className="muted">Monthly cost rollup across all projects.</p>
        </div>
      </div>
      <div className="metric-grid">
        <MetricCard
          label="Total AI cost"
          value={formatCurrency(data.cost?.totalAiCost, data.currency)}
        />
        <MetricCard label="Prompts" value={formatNumber(data.activity?.promptCount)} />
        <MetricCard
          label="Agent time"
          value={formatDurationMs(data.activity?.agentDurationMilliseconds)}
        />
        <MetricCard label="Projects" value={formatNumber(data.projects.length)} />
      </div>
      {data.projects.length === 0 ? (
        <EmptyState message="No project costs recorded this month." />
      ) : (
        <>
          <div className="chart-grid">
            <ChartCard title="Cost by project">
              <NamedBarChart
                data={data.projects.map((p) => ({
                  name: p.projectName,
                  value: p.totalAiCost,
                }))}
                valueKey="value"
                valueLabel="Cost"
              />
            </ChartCard>
          </div>
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>Project</th>
                  <th>Client</th>
                  <th>Prompts</th>
                  <th>Usage</th>
                  <th>Subscription</th>
                  <th>Total</th>
                </tr>
              </thead>
              <tbody>
                {data.projects.map((project) => (
                  <tr key={project.projectId}>
                    <td>
                      <Link to={`/projects/${project.projectId}`}>{project.projectName}</Link>
                    </td>
                    <td>{project.clientName?.trim() || '—'}</td>
                    <td>{formatNumber(project.promptCount)}</td>
                    <td>{formatCurrency(project.usageBasedCursorCost, project.currency)}</td>
                    <td>{formatCurrency(project.subscriptionAllocation, project.currency)}</td>
                    <td>{formatCurrency(project.totalAiCost, project.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}

function EditorComparisonReportView({
  query,
}: {
  query: ReturnType<typeof useEditorComparisonReportQuery>;
}) {
  if (query.isLoading) return <LoadingState label="Loading editor comparison…" />;
  if (query.error) {
    return (
      <ErrorState
        message={query.error instanceof Error ? query.error.message : 'Failed to load report'}
      />
    );
  }
  const editors = query.data?.editors ?? [];
  if (editors.length === 0) {
    return <EmptyState message="No editor activity for this range." />;
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h3>Editor comparison</h3>
          <p className="muted">Prompt and agent activity by editor.</p>
        </div>
      </div>
      <div className="chart-grid">
        <ChartCard title="Prompts by editor">
          <NamedBarChart
            data={editors.map((e) => ({ name: e.name, value: e.promptCount }))}
            valueKey="value"
            valueLabel="Prompts"
          />
        </ChartCard>
        <ChartCard title="Agent time by editor">
          <NamedBarChart
            data={editors.map((e) => ({
              name: e.name,
              value: Math.round(e.agentDurationMilliseconds / 1000),
            }))}
            valueKey="value"
            valueLabel="Seconds"
          />
        </ChartCard>
      </div>
      <div className="table-wrap">
        <table className="data">
          <thead>
            <tr>
              <th>Editor</th>
              <th>Prompts</th>
              <th>Agent runs</th>
              <th>Agent time</th>
              <th>Active time</th>
            </tr>
          </thead>
          <tbody>
            {editors.map((editor) => (
              <tr key={editor.name}>
                <td>{editor.name}</td>
                <td>{formatNumber(editor.promptCount)}</td>
                <td>{formatNumber(editor.agentRuns)}</td>
                <td>{formatDurationMs(editor.agentDurationMilliseconds)}</td>
                <td>{formatDurationSeconds(editor.activeProjectTimeSeconds)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
