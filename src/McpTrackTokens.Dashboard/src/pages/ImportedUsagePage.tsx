import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  useDeleteUnallocatedUsageMutation,
  useImportedUsageQuery,
  useReconciliationMutation,
  useUnallocatedQuery,
} from '../api/hooks';
import type { ReconciliationResultDto } from '../api/types';
import { ImportUploadMapPanel } from '../components/ImportUploadMapPanel';
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
  const unallocated = useUnallocatedQuery(range.fromUtc, range.toUtc);
  const allocateAll = useReconciliationMutation();
  const deleteUnallocated = useDeleteUnallocatedUsageMutation();
  const [lastResult, setLastResult] = useState<ReconciliationResultDto | null>(null);
  const [deleteMessage, setDeleteMessage] = useState<string | null>(null);

  const report = imported.data;
  const items = report?.items ?? [];
  const failed = lastResult?.unallocated ?? [];
  const unallocatedCount = unallocated.data?.usage?.count ?? 0;

  return (
    <Page>
      <ImportUploadMapPanel />

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
            {unallocatedCount > 0 ? (
              <StatusBadge
                label={`${formatNumber(unallocatedCount)} unallocated`}
                tone="warning"
              />
            ) : null}
            <button
              type="button"
              className="btn"
              disabled={allocateAll.isPending || items.length === 0}
              onClick={() => {
                setLastResult(null);
                setDeleteMessage(null);
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
            <button
              type="button"
              className="btn btn-danger"
              disabled={
                deleteUnallocated.isPending || unallocated.isLoading || unallocatedCount === 0
              }
              onClick={() => {
                const confirmed = window.confirm(
                  `Delete ${formatNumber(unallocatedCount)} unallocated usage row${unallocatedCount === 1 ? '' : 's'} from the last 90 days? Allocated rows are kept.`,
                );
                if (!confirmed) return;
                setLastResult(null);
                setDeleteMessage(null);
                deleteUnallocated.mutate(
                  { fromUtc: range.fromUtc, toUtc: range.toUtc },
                  {
                    onSuccess: (result) => {
                      setDeleteMessage(
                        result.deletedCount === 0
                          ? 'No unallocated usage rows to delete.'
                          : `Deleted ${formatNumber(result.deletedCount)} unallocated usage row${result.deletedCount === 1 ? '' : 's'}.`,
                      );
                    },
                  },
                );
              }}
            >
              {deleteUnallocated.isPending ? 'Deleting…' : 'Delete unallocated usage'}
            </button>
          </div>
        </div>

        {imported.isLoading ? <LoadingState label="Loading imported usage…" /> : null}

        {imported.error ? (
          <ErrorState
            message={
              imported.error instanceof Error
                ? imported.error.message
                : 'Failed to load imported usage'
            }
          />
        ) : null}

        {deleteUnallocated.isError ? (
          <ErrorState
            message={
              deleteUnallocated.error instanceof Error
                ? deleteUnallocated.error.message
                : 'Delete unallocated usage failed'
            }
          />
        ) : null}

        {deleteMessage ? <div className="panel">{deleteMessage}</div> : null}

        {!imported.isLoading && !imported.error ? (
          <>
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
              <article className="metric-card">
                <div className="label">Calculated cost</div>
                <div className="value">
                  {formatCurrency(
                    report?.totalCalculatedTokenCost ?? 0,
                    report?.currency ?? 'USD',
                  )}
                </div>
              </article>
            </div>

            {allocateAll.isError ? (
              <ErrorState
                message={
                  allocateAll.error instanceof Error
                    ? allocateAll.error.message
                    : 'Allocate all failed'
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
                        <th>Calculated cost</th>
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
                          <td>{formatCurrency(row.calculatedTokenCost ?? 0)}</td>
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
              <EmptyState message="No imported usage rows in this range. Upload a Cursor CSV above." />
            ) : (
              <div className="table-wrap">
                <table className="data">
                  <thead>
                    <tr>
                      <th>When</th>
                      <th>Model</th>
                      <th>Total Tokens</th>
                      <th>Cost</th>
                      <th>Calculated cost</th>
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
                          {formatCurrency(item.calculatedTokenCost ?? 0, item.currency)}
                        </td>
                        <td>
                          {item.projectId ? (
                            <Link to={`/projects/${item.projectId}`}>
                              {item.projectName ?? item.projectId}
                            </Link>
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
          </>
        ) : null}
      </section>
    </Page>
  );
}
