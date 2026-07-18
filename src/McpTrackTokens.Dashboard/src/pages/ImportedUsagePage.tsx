import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useImportedUsageQuery, useReconciliationMutation } from '../api/hooks';
import type { ReconciliationResultDto } from '../api/types';
import { ErrorState, EmptyState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
import {
  formatCurrency,
  formatDateTime,
  formatNumber,
  lastDaysRange,
} from '../utils/format';

export function ImportedUsagePage() {
  const range = useMemo(() => lastDaysRange(90), []);
  const imported = useImportedUsageQuery(range.fromUtc, range.toUtc);
  const allocateAll = useReconciliationMutation();
  const [lastResult, setLastResult] = useState<ReconciliationResultDto | null>(null);

  if (imported.isLoading) {
    return <LoadingState label="Loading imported usage…" />;
  }

  if (imported.error) {
    return (
      <ErrorState
        message={
          imported.error instanceof Error
            ? imported.error.message
            : 'Failed to load imported usage'
        }
      />
    );
  }

  const report = imported.data;
  const items = report?.items ?? [];
  const failed = lastResult?.unallocated ?? [];

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Imported usage</h2>
            <p>
              All Cursor usage rows imported in the last 90 days. Allocate all links each row
              (Total Tokens &gt; 0) to the closest prompt at or before its timestamp.
            </p>
          </div>
          <div className="row">
            <StatusBadge label={`${formatNumber(report?.count ?? 0)} rows`} tone="info" />
            <button
              type="button"
              className="btn"
              disabled={allocateAll.isPending || items.length === 0}
              onClick={() => {
                setLastResult(null);
                allocateAll.mutate(
                  {
                    fromUtc: range.fromUtc,
                    toUtc: range.toUtc,
                    dryRun: false,
                    includeLowConfidence: true,
                  },
                  { onSuccess: (result) => setLastResult(result) },
                );
              }}
            >
              {allocateAll.isPending ? 'Allocating…' : 'Allocate all'}
            </button>
            <Link to="/imports" className="btn btn-secondary">
              Import more
            </Link>
          </div>
        </div>

        <div className="metric-grid">
          <article className="metric-card">
            <div className="label">Total tokens</div>
            <div className="value">{formatNumber(report?.totalTokens ?? 0)}</div>
          </article>
          <article className="metric-card">
            <div className="label">Reported cost</div>
            <div className="value">
              {formatCurrency(report?.totalCost ?? 0, report?.currency ?? 'USD')}
            </div>
          </article>
        </div>

        {allocateAll.isError ? (
          <ErrorState
            message={
              allocateAll.error instanceof Error ? allocateAll.error.message : 'Allocate all failed'
            }
          />
        ) : null}

        {lastResult ? (
          <div className="panel stack">
            <p>
              Processed {formatNumber(lastResult.processedCount)} · allocated{' '}
              {formatNumber(lastResult.allocatedCount)} · could not allocate{' '}
              {formatNumber(lastResult.unallocatedCount)}
              {lastResult.skippedCount > 0
                ? ` · skipped ${formatNumber(lastResult.skippedCount)}`
                : ''}
            </p>
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
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  {failed.map((row, index) => (
                    <tr key={`${row.usageRecordId}-${index}`}>
                      <td>{formatDateTime(row.timestampUtc)}</td>
                      <td>{row.model ?? '—'}</td>
                      <td>{formatNumber(row.allocatedTotalTokens)}</td>
                      <td>{formatCurrency(row.allocatedCost)}</td>
                      <td>{row.reason ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ) : null}

        {lastResult && failed.length === 0 && lastResult.processedCount > 0 ? (
          <div className="panel">All eligible usage rows were allocated.</div>
        ) : null}

        {items.length === 0 ? (
          <EmptyState message="No imported usage rows in this range. Upload a Cursor CSV from Imports." />
        ) : (
          <div className="table-wrap">
            <table className="data">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Model</th>
                  <th>Total Tokens</th>
                  <th>Cost</th>
                  <th>Project</th>
                  <th>Prompt link</th>
                  <th>Method</th>
                  <th>Imported</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{formatDateTime(item.timestampUtc)}</td>
                    <td>{item.model ?? '—'}</td>
                    <td>{formatNumber(item.totalTokens)}</td>
                    <td>{formatCurrency(item.reportedCost, item.currency)}</td>
                    <td>
                      {item.projectId ? (
                        <Link to={`/projects/${item.projectId}`}>{item.projectName ?? item.projectId}</Link>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td className="mono">{item.activityEventId ?? '—'}</td>
                    <td>{item.attributionMethod ?? '—'}</td>
                    <td>{formatDateTime(item.importedAtUtc)}</td>
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
