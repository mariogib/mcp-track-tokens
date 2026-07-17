import { useMemo, useState } from 'react';
import {
  useAllocateUsageMutation,
  useProjectsQuery,
  useReconciliationMutation,
  useUnallocatedQuery,
} from '../api/hooks';
import type { UnallocatedItemDto, UnallocatedUsageReport, UsageAttributionRow } from '../api/types';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
import {
  formatCurrency,
  formatDateTime,
  formatNumber,
  lastDaysRange,
} from '../utils/format';

function asItems(data: UnallocatedItemDto[] | UnallocatedUsageReport | undefined): UnallocatedItemDto[] {
  if (!data) return [];
  if (Array.isArray(data)) return data;
  return data.items ?? [];
}

function confidenceTone(value?: string | null): 'success' | 'warning' | 'danger' | 'neutral' {
  const c = (value ?? '').toLowerCase();
  if (c.includes('high')) return 'success';
  if (c.includes('medium')) return 'warning';
  if (c.includes('low')) return 'danger';
  return 'neutral';
}

export function ReconciliationPage() {
  const range = useMemo(() => lastDaysRange(30), []);
  const [includeLowConfidence, setIncludeLowConfidence] = useState(true);
  const [dryRun, setDryRun] = useState(true);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState('');
  const [percentage, setPercentage] = useState(100);
  const [reason, setReason] = useState('');
  const [audit, setAudit] = useState<UsageAttributionRow[]>([]);

  const unallocated = useUnallocatedQuery(range.fromUtc, range.toUtc);
  const projects = useProjectsQuery();
  const reconcile = useReconciliationMutation();
  const allocate = useAllocateUsageMutation();

  const items = asItems(unallocated.data);
  const selected = items.find((i) => i.id === selectedId) ?? null;

  if (unallocated.isLoading) {
    return <LoadingState label="Loading unallocated usage…" />;
  }

  if (unallocated.error) {
    return (
      <ErrorState
        message={
          unallocated.error instanceof Error
            ? unallocated.error.message
            : 'Failed to load unallocated items'
        }
      />
    );
  }

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Unallocated usage</h2>
            <p>Review candidates, confidence, and allocate manually when needed.</p>
          </div>
          <div className="row">
            <label className="row">
              <input
                type="checkbox"
                checked={dryRun}
                onChange={(e) => setDryRun(e.target.checked)}
              />
              Dry run
            </label>
            <label className="row">
              <input
                type="checkbox"
                checked={includeLowConfidence}
                onChange={(e) => setIncludeLowConfidence(e.target.checked)}
              />
              Include low confidence
            </label>
            <button
              type="button"
              className="btn"
              disabled={reconcile.isPending}
              onClick={() =>
                reconcile.mutate(
                  {
                    fromUtc: range.fromUtc,
                    toUtc: range.toUtc,
                    dryRun,
                    includeLowConfidence,
                  },
                  {
                    onSuccess: (result) => {
                      setAudit((prev) => [...result.attributions, ...prev].slice(0, 100));
                    },
                  },
                )
              }
            >
              Run reconciliation
            </button>
          </div>
        </div>

        {reconcile.isError ? (
          <ErrorState
            message={
              reconcile.error instanceof Error ? reconcile.error.message : 'Reconciliation failed'
            }
          />
        ) : null}

        {reconcile.isSuccess ? (
          <div className="panel">
            Processed {formatNumber(reconcile.data.processedCount)} · allocated{' '}
            {formatNumber(reconcile.data.allocatedCount)} · still unallocated{' '}
            {formatNumber(reconcile.data.unallocatedCount)}
            {reconcile.data.dryRun ? ' (dry run)' : ''}
          </div>
        ) : null}

        {items.length === 0 ? (
          <EmptyState message="No unallocated usage in the last 30 days." />
        ) : (
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Kind</th>
                  <th>Model</th>
                  <th>Cost</th>
                  <th>Candidate</th>
                  <th>Confidence</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{formatDateTime(item.timestampUtc)}</td>
                    <td>{item.kind}</td>
                    <td>{item.model ?? '—'}</td>
                    <td>{formatCurrency(item.reportedCost, item.currency ?? 'USD')}</td>
                    <td>{item.suggestedProjectName ?? '—'}</td>
                    <td>
                      <StatusBadge
                        label={item.suggestedConfidence ?? 'n/a'}
                        tone={confidenceTone(item.suggestedConfidence)}
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-secondary"
                        onClick={() => {
                          setSelectedId(item.id);
                          setProjectId(item.suggestedProjectId ?? '');
                          setPercentage(100);
                          setReason(item.reason ?? '');
                        }}
                      >
                        Allocate
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {selected ? (
        <section className="page-section">
          <div className="panel stack">
            <h3 className="panel-title">Manual allocation</h3>
            <p className="mono">{selected.id}</p>
            <div className="field-row">
              <div className="field">
                <label htmlFor="alloc-project">Project</label>
                <select
                  id="alloc-project"
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                >
                  <option value="">Select project…</option>
                  {(projects.data ?? []).map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor="alloc-pct">Percentage</label>
                <input
                  id="alloc-pct"
                  type="number"
                  min={1}
                  max={100}
                  value={percentage}
                  onChange={(e) => setPercentage(Number(e.target.value))}
                />
              </div>
            </div>
            <div className="field">
              <label htmlFor="alloc-reason">Reason</label>
              <textarea
                id="alloc-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
            <div className="row">
              <button
                type="button"
                className="btn"
                disabled={!projectId || allocate.isPending}
                onClick={() =>
                  allocate.mutate(
                    {
                      usageRecordId: selected.id,
                      projectAllocations: [{ projectId, percentage }],
                      reason: reason || null,
                      reviewedBy: 'dashboard',
                      replaceExisting: true,
                    },
                    {
                      onSuccess: (rows) => {
                        setAudit((prev) => [...rows, ...prev].slice(0, 100));
                        setSelectedId(null);
                      },
                    },
                  )
                }
              >
                Save allocation
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                onClick={() => setSelectedId(null)}
              >
                Cancel
              </button>
            </div>
            {allocate.isError ? (
              <ErrorState
                message={allocate.error instanceof Error ? allocate.error.message : 'Allocate failed'}
              />
            ) : null}
          </div>
        </section>
      ) : null}

      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Audit trail</h2>
            <p>Recent attribution decisions from this session.</p>
          </div>
        </div>
        {audit.length === 0 ? (
          <EmptyState message="No attribution actions yet in this session." />
        ) : (
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Project</th>
                  <th>Method</th>
                  <th>Confidence</th>
                  <th>Cost</th>
                  <th>Reason</th>
                </tr>
              </thead>
              <tbody>
                {audit.map((row, index) => (
                  <tr key={`${row.usageRecordId}-${row.attributionId ?? index}`}>
                    <td>{formatDateTime(row.timestampUtc)}</td>
                    <td>{row.projectName ?? row.projectId ?? '—'}</td>
                    <td>{row.attributionMethod}</td>
                    <td>
                      <StatusBadge label={row.confidence} tone={confidenceTone(row.confidence)} />
                    </td>
                    <td>{formatCurrency(row.allocatedCost)}</td>
                    <td>{row.reason ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </Page>
  );
}
