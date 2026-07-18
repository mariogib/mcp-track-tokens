import { MetricCard } from '../components/MetricCard';
import { ErrorState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import {
  useActiveSessionQuery,
  useHealthQuery,
  useReportsSummaryQuery,
  useStatusQuery,
  useUnallocatedQuery,
} from '../api/hooks';
import {
  formatCurrency,
  formatDateTime,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
  lastDaysRange,
} from '../utils/format';
import { Page } from '../layout/AppLayout';

export function OverviewPage() {
  const now = new Date();
  const year = now.getUTCFullYear();
  const month = now.getUTCMonth() + 1;
  const range = lastDaysRange(30);

  const health = useHealthQuery();
  const status = useStatusQuery();
  const summary = useReportsSummaryQuery(year, month);
  const session = useActiveSessionQuery();
  const unallocated = useUnallocatedQuery(range.fromUtc, range.toUtc);

  const loading = status.isLoading || summary.isLoading;
  const error = status.error || summary.error;

  if (loading) {
    return <LoadingState label="Loading overview…" />;
  }

  if (error) {
    return (
      <ErrorState
        message={
          error instanceof Error
            ? error.message
            : 'Unable to load overview. Check the API server and API key.'
        }
      />
    );
  }

  const activity = summary.data?.activity;
  const cost = summary.data?.cost;
  const unallocatedActivityCount =
    status.data?.unallocatedEventCount ?? unallocated.data?.activity?.length ?? 0;
  const unallocatedUsageCount =
    status.data?.unallocatedUsageCount ?? unallocated.data?.usage?.count ?? 0;
  const healthy =
    health.data?.healthy === true ||
    health.data?.status === 'Healthy' ||
    (health.isSuccess && !health.isError);

  return (
    <Page>
      <section className="page-section" aria-labelledby="overview-metrics">
        <div className="section-header">
          <div>
            <h2 id="overview-metrics">Today & this month</h2>
            <p>Active session context and cost signals from the local tracker.</p>
          </div>
          <StatusBadge
            label={healthy ? 'Healthy' : 'Degraded'}
            tone={healthy ? 'success' : 'danger'}
          />
        </div>

        <div className="metric-grid">
          <MetricCard
            label="Active project"
            value={status.data?.currentProject?.name ?? 'None'}
            hint={
              status.data?.activeSessionEditor
                ? `Editor: ${status.data.activeSessionEditor}`
                : 'No active editor session'
            }
          />
          <MetricCard
            label="Active session"
            value={session.data?.id ? session.data.id.slice(0, 8) : 'Idle'}
            hint={
              session.data?.startedAtUtc
                ? `Started ${formatDateTime(session.data.startedAtUtc)}`
                : 'Waiting for heartbeat'
            }
          />
          <MetricCard
            label="Prompts (month)"
            value={formatNumber(activity?.promptCount)}
            hint={`${formatNumber(activity?.agentRuns)} agent runs`}
          />
          <MetricCard
            label="Agent time"
            value={formatDurationMs(activity?.agentDurationMilliseconds)}
            hint="Sum of agent durations"
          />
          <MetricCard
            label="Active project time"
            value={formatDurationSeconds(activity?.activeProjectTimeSeconds)}
            hint="Merged activity windows"
          />
          <MetricCard
            label="Cursor cost (month)"
            value={formatCurrency(cost?.totalAiCost, cost?.currency ?? summary.data?.currency)}
            hint={`Usage ${formatCurrency(cost?.usageBasedCost, cost?.currency)} · Sub ${formatCurrency(cost?.subscriptionAllocation, cost?.currency)}`}
          />
          <MetricCard
            label="Unallocated activity"
            value={formatNumber(unallocatedActivityCount)}
            hint="Click to assign events to projects"
            to="/unallocated"
          />
          <MetricCard
            label="Unallocated usage"
            value={formatNumber(unallocatedUsageCount)}
            hint={formatCurrency(cost?.unallocatedCost, cost?.currency)}
            to="/reconciliation"
          />
        </div>
      </section>

      <section className="page-section" aria-labelledby="overview-health">
        <div className="section-header">
          <div>
            <h2 id="overview-health">Server health</h2>
            <p>Database path, queue depth, and latest ingest.</p>
          </div>
        </div>
        <div className="panel stack">
          <div className="row">
            <StatusBadge
              label={status.data?.isHealthy ? 'Tracker OK' : 'Tracker issue'}
              tone={status.data?.isHealthy ? 'success' : 'warning'}
            />
            <span className="mono">{status.data?.databasePath ?? '—'}</span>
          </div>
          <div className="field-row">
            <div>
              <div className="label">Provider</div>
              <strong>{status.data?.databaseProvider ?? '—'}</strong>
            </div>
            <div>
              <div className="label">Queued events</div>
              <strong>{formatNumber(status.data?.queuedEventCount)}</strong>
            </div>
            <div>
              <div className="label">Last event</div>
              <strong>{formatDateTime(status.data?.lastEventAtUtc)}</strong>
            </div>
            <div>
              <div className="label">Last Cursor import</div>
              <strong>
                {formatDateTime(status.data?.lastCursorImportAtUtc)}{' '}
                {status.data?.lastCursorImportStatus
                  ? `(${status.data.lastCursorImportStatus})`
                  : ''}
              </strong>
            </div>
          </div>
        </div>
      </section>
    </Page>
  );
}
