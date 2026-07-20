import { useMemo, useState } from 'react';
import { useReconciliationMutation, useUnallocatedQuery } from '../api/hooks';
import type { ReconciliationResultDto, UnallocatedItemDto, UsageAttributionRow } from '../api/types';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
import {
  formatCurrency,
  formatDateTime,
  formatNumber,
  lastDaysRange,
} from '../utils/format';

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
  const [dryRun, setDryRun] = useState(false);
  const [lastResult, setLastResult] = useState<ReconciliationResultDto | null>(null);
  const [audit, setAudit] = useState<UsageAttributionRow[]>([]);

  const unallocated = useUnallocatedQuery(range.fromUtc, range.toUtc);
  const reconcile = useReconciliationMutation();

  const items: UnallocatedItemDto[] = unallocated.data?.usage?.items ?? [];
  const failed = lastResult?.unallocated ?? [];

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
            <p>
              Allocate all links each imported row (Total Tokens &gt; 0) to the closest prompt at
              or before the usage timestamp (second precision).
            </p>
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
              onClick={() => {
                setLastResult(null);
                reconcile.mutate(
                  {
                    fromUtc: range.fromUtc,
                    toUtc: range.toUtc,
                    dryRun,
                    includeLowConfidence,
                  },
                  {
                    onSuccess: (result) => {
                      setLastResult(result);
                      setAudit((prev) => [...result.attributions, ...prev].slice(0, 100));
                    },
                  },
                );
              }}
            >
              {reconcile.isPending ? 'Allocating…' : 'Allocate all'}
            </button>
          </div>
        </div>

        {reconcile.isError ? (
          <ErrorState
            message={
              reconcile.error instanceof Error ? reconcile.error.message : 'Allocate all failed'
            }
          />
        ) : null}

        {lastResult ? (
          <div className="panel">
            Processed {formatNumber(lastResult.processedCount)} · allocated{' '}
            {formatNumber(lastResult.allocatedCount)} · could not allocate{' '}
            {formatNumber(lastResult.unallocatedCount)}
            {lastResult.dryRun ? ' (dry run)' : ''}
          </div>
        ) : null}

        {lastResult && failed.length > 0 ? (
          <div className="stack" style={{ marginTop: '1rem' }}>
            <h3>Could not allocate</h3>
            <p>No prompt with a project at or before these usage timestamps.</p>
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>When</th>
                    <th>Model</th>
                    <th>Total Tokens</th>
                    <th>Cost</th>
                    <th>Calculated cost</th>
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  {failed.map((row, index) => (
                    <tr key={`${row.usageRecordId}-fail-${index}`}>
                      <td>{formatDateTime(row.timestampUtc)}</td>
                      <td>{row.model ?? '—'}</td>
                      <td>{formatNumber(row.allocatedTotalTokens)}</td>
                      <td>{formatCurrency(row.allocatedCost)}</td>
                      <td>{formatCurrency(row.calculatedTokenCost ?? 0)}</td>
                      <td>{row.reason ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
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
                  <th>Total Tokens</th>
                  <th>Cost</th>
                  <th>Calculated cost</th>
                  <th>Candidate</th>
                  <th>Confidence</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{formatDateTime(item.timestampUtc)}</td>
                    <td>{item.kind}</td>
                    <td>{item.model ?? '—'}</td>
                    <td>{formatNumber(item.totalTokens ?? 0)}</td>
                    <td>{formatCurrency(item.reportedCost ?? 0, item.currency ?? 'USD')}</td>
                    <td>
                      {formatCurrency(item.calculatedTokenCost ?? 0, item.currency ?? 'USD')}
                    </td>
                    <td>{item.suggestedProjectName ?? '—'}</td>
                    <td>
                      <StatusBadge
                        label={item.suggestedConfidence ?? 'n/a'}
                        tone={confidenceTone(item.suggestedConfidence)}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

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
                  <th>Total Tokens</th>
                  <th>Cost</th>
                  <th>Calculated cost</th>
                  <th>Prompt</th>
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
                    <td>{formatNumber(row.allocatedTotalTokens)}</td>
                    <td>{formatCurrency(row.allocatedCost)}</td>
                    <td>{formatCurrency(row.calculatedTokenCost ?? 0)}</td>
                    <td className="mono">{row.activityEventId ?? '—'}</td>
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
