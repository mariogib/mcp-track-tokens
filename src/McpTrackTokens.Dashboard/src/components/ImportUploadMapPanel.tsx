import { useMemo, useState } from 'react';
import { api } from '../api/client';
import { useImportUploadMutation } from '../api/hooks';
import type { ImportPreviewDto, ImportResultDto } from '../api/types';
import { EmptyState, ErrorState } from './States';
import { MetricCard, Panel } from './MetricCard';
import { StatusBadge } from './StatusBadge';
import { TextLink } from '../shared/adminUi';
import { formatNumber } from '../utils/format';

const TARGET_FIELDS = [
  'TimestampUtc',
  'Model',
  'Provider',
  'InputTokens',
  'OutputTokens',
  'TotalTokens',
  'CachedInputTokens',
  'ReportedCost',
  'Currency',
  'ExternalRequestId',
  'ExternalSessionId',
  'ExternalRecordId',
  'ignore',
];

type Step = 'upload' | 'map' | 'result';

export function ImportUploadMapPanel() {
  const [step, setStep] = useState<Step>('upload');
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreviewDto | null>(null);
  const [mappings, setMappings] = useState<Record<string, string>>({});
  const [result, setResult] = useState<ImportResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const importMutation = useImportUploadMutation();

  const columns = useMemo(() => preview?.columns ?? [], [preview?.columns]);

  const mappingRows = useMemo(
    () =>
      columns.map((column) => ({
        column,
        target: mappings[column] ?? preview?.columnMappings?.[column] ?? 'ignore',
      })),
    [columns, mappings, preview],
  );

  async function handlePreview() {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const data = await api.previewImportUpload(file);
      setPreview(data);
      setMappings({ ...data.columnMappings });
      setStep('map');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Preview failed');
    } finally {
      setBusy(false);
    }
  }

  async function runImport(dryRun: boolean) {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const data = await importMutation.mutateAsync({
        file,
        dryRun,
        columnMappings: mappings,
      });
      setResult(data);
      setStep('result');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Import failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>Upload &amp; map</h2>
          <p>
            Upload CSV or JSON, preview columns, map fields, then import. Every row with Total
            Tokens &gt; 0 is imported (cost may be Included / 0). Re-importing the same file adds
            any missing rows.
          </p>
          <p>
            <TextLink href="https://cursor.com/dashboard/usage" external>
              Open Cursor usage dashboard
            </TextLink>{' '}
            to export your usage file.
          </p>
        </div>
        <StatusBadge
          label={step === 'upload' ? 'Step 1' : step === 'map' ? 'Step 2' : 'Complete'}
          tone="info"
        />
      </div>

      {error ? <ErrorState message={error} /> : null}

      {step === 'upload' && (
        <Panel className="stack">
          <div className="field">
            <label htmlFor="import-file">Usage export file</label>
            <input
              id="import-file"
              type="file"
              accept=".csv,.json,text/csv,application/json"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                setPreview(null);
                setResult(null);
              }}
            />
          </div>
          <div className="row">
            <button
              type="button"
              className="btn"
              disabled={!file || busy}
              onClick={() => void handlePreview()}
            >
              Preview columns
            </button>
          </div>
        </Panel>
      )}

      {step === 'map' && preview && (
        <div className="stack">
          <div className="metric-grid">
            <MetricCard label="Received" value={formatNumber(preview.receivedCount)} />
            <MetricCard label="Valid" value={formatNumber(preview.validCount)} />
            <MetricCard label="Duplicates" value={formatNumber(preview.duplicateCount)} />
            <MetricCard label="Invalid" value={formatNumber(preview.invalidCount)} />
          </div>

          <Panel className="stack">
            <h3 className="panel-title">
              {preview.fileName} · detected {preview.detectedFormat}
            </h3>
            {preview.warnings?.length ? (
              <ul>
                {preview.warnings.map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            ) : null}

            {columns.length === 0 ? (
              <EmptyState message="No columns detected in the upload." />
            ) : (
              <div className="table-wrap">
                <table className="data">
                  <thead>
                    <tr>
                      <th>Source column</th>
                      <th>Map to</th>
                    </tr>
                  </thead>
                  <tbody>
                    {mappingRows.map((row) => (
                      <tr key={row.column}>
                        <td className="mono">{row.column}</td>
                        <td>
                          <select
                            aria-label={`Map ${row.column}`}
                            value={row.target}
                            onChange={(e) =>
                              setMappings((prev) => ({
                                ...prev,
                                [row.column]: e.target.value,
                              }))
                            }
                          >
                            {TARGET_FIELDS.map((field) => (
                              <option key={field} value={field}>
                                {field}
                              </option>
                            ))}
                          </select>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="row">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setStep('upload');
                  setPreview(null);
                }}
              >
                Back
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={busy}
                onClick={() => void runImport(true)}
              >
                Dry import
              </button>
              <button
                type="button"
                className="btn"
                disabled={busy}
                onClick={() => void runImport(false)}
              >
                Complete import
              </button>
            </div>
          </Panel>
        </div>
      )}

      {step === 'result' && result && (
        <Panel className="stack">
          <div className="row">
            <StatusBadge
              label={result.dryRun ? 'Dry run' : result.status}
              tone={result.failedCount > 0 ? 'warning' : 'success'}
            />
            <span className="mono">{result.fileName}</span>
          </div>
          <div className="metric-grid">
            <MetricCard label="Received" value={formatNumber(result.receivedCount)} />
            <MetricCard label="Imported" value={formatNumber(result.importedCount)} />
            <MetricCard label="Duplicates" value={formatNumber(result.duplicateCount)} />
            <MetricCard label="Errors" value={formatNumber(result.failedCount)} />
          </div>
          {result.errorSummary ? <ErrorState message={result.errorSummary} /> : null}
          <div className="row">
            <button
              type="button"
              className="btn"
              onClick={() => {
                setStep('upload');
                setFile(null);
                setPreview(null);
                setResult(null);
              }}
            >
              Import another file
            </button>
          </div>
        </Panel>
      )}
    </section>
  );
}
