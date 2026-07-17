import { useEffect, useState } from 'react';
import {
  API_KEY_STORAGE,
  getStoredApiKey,
  setStoredApiKey,
} from '../api/client';
import {
  useApiKeysQuery,
  useCreateApiKeyMutation,
  useIntegrationsQuery,
  useRevokeApiKeyMutation,
  useSettingsQuery,
  useStatusQuery,
  useUpdateSettingsMutation,
} from '../api/hooks';
import type { SettingsDto, UpdateSettingsRequest } from '../api/types';
import { ErrorState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
import { formatDateTime } from '../utils/format';

const ALLOCATION_METHODS = [
  'NotAllocated',
  'EqualAcrossActiveProjects',
  'ByActiveProjectTime',
  'ByPromptCount',
  'ByAgentDuration',
  'ManualPercentage',
  'TimeWindowMatch',
  'ProportionalTimeAllocation',
];

function toDraft(settings: SettingsDto): UpdateSettingsRequest & {
  inactivityThresholdMinutes: number;
  defaultCurrency: string;
  cursorSubscriptionAmount: number;
  cursorSubscriptionCurrency: string;
  cursorAllocationMethod: string;
  storePromptContent: boolean;
  storeResponseContent: boolean;
  enablePromptHashing: boolean;
  exportPath: string;
  dataRetentionDays: number | null;
  autoCreateProjects: boolean;
} {
  return {
    inactivityThresholdMinutes: settings.inactivityThresholdMinutes,
    defaultCurrency: settings.defaultCurrency,
    cursorSubscriptionAmount: settings.cursorSubscriptionAmount,
    cursorSubscriptionCurrency: settings.cursorSubscriptionCurrency,
    cursorAllocationMethod: settings.cursorAllocationMethod,
    storePromptContent: settings.storePromptContent,
    storeResponseContent: settings.storeResponseContent,
    enablePromptHashing: settings.enablePromptHashing,
    exportPath: settings.exportPath,
    dataRetentionDays: settings.dataRetentionDays ?? null,
    autoCreateProjects: settings.autoCreateProjects,
  };
}

export function SettingsPage() {
  const settings = useSettingsQuery();
  const status = useStatusQuery();
  const apiKeys = useApiKeysQuery();
  const integrations = useIntegrationsQuery();
  const updateSettings = useUpdateSettingsMutation();
  const createKey = useCreateApiKeyMutation();
  const revokeKey = useRevokeApiKeyMutation();

  const [draft, setDraft] = useState<ReturnType<typeof toDraft> | null>(null);
  const [localKey, setLocalKey] = useState(() => getStoredApiKey() ?? '');
  const [newKeyName, setNewKeyName] = useState('Dashboard');
  const [createdPlaintext, setCreatedPlaintext] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (settings.data) {
      setDraft(toDraft(settings.data));
    }
  }, [settings.data]);

  const apiKeyPanel = (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>API key management</h2>
          <p>
            Dashboard auth uses localStorage key <span className="mono">{API_KEY_STORAGE}</span>.
            Paste a key first if the rest of this page cannot load.
          </p>
        </div>
      </div>

      <div className="panel stack">
        <div className="field-row">
          <div className="field">
            <label htmlFor="local-api-key">Bearer key for this browser</label>
            <input
              id="local-api-key"
              className="mono"
              type="password"
              value={localKey}
              onChange={(e) => setLocalKey(e.target.value)}
              autoComplete="off"
              placeholder="OverTheMoon or mtt_…"
            />
          </div>
        </div>
        <div className="row">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              setStoredApiKey(localKey.trim() || null);
              setMessage('Local API key saved. Reloading…');
              window.location.reload();
            }}
          >
            Save local key
          </button>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => {
              setLocalKey('');
              setStoredApiKey(null);
              setMessage('Local API key cleared.');
            }}
          >
            Clear local key
          </button>
        </div>
        {message ? <p>{message}</p> : null}
        {createdPlaintext ? (
          <div className="warning-banner" role="status">
            New key (copy now): <code className="mono">{createdPlaintext}</code>
          </div>
        ) : null}
      </div>
    </section>
  );

  if (settings.isLoading && !settings.isError && !draft) {
    return (
      <Page>
        {apiKeyPanel}
        <LoadingState label="Loading settings…" />
      </Page>
    );
  }

  if (settings.error && !draft) {
    return (
      <Page>
        {apiKeyPanel}
        <ErrorState
          message={
            settings.error instanceof Error ? settings.error.message : 'Failed to load settings'
          }
          error={settings.error}
        />
      </Page>
    );
  }

  if (!draft) {
    return (
      <Page>
        {apiKeyPanel}
        <LoadingState label="Loading settings…" />
      </Page>
    );
  }

  const contentWarning = draft.storePromptContent || draft.storeResponseContent;

  return (
    <Page>
      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Tracking preferences</h2>
            <p>Inactivity, currency, subscription allocation, and retention.</p>
          </div>
        </div>

        <form
          className="panel stack"
          onSubmit={(e) => {
            e.preventDefault();
            setMessage(null);
            updateSettings.mutate(draft, {
              onSuccess: () => setMessage('Settings saved.'),
              onError: (err) =>
                setMessage(err instanceof Error ? err.message : 'Failed to save settings'),
            });
          }}
        >
          <div className="field-row">
            <div className="field">
              <label htmlFor="inactivity">Inactivity threshold (minutes)</label>
              <input
                id="inactivity"
                type="number"
                min={1}
                value={draft.inactivityThresholdMinutes}
                onChange={(e) =>
                  setDraft((d) =>
                    d ? { ...d, inactivityThresholdMinutes: Number(e.target.value) } : d,
                  )
                }
              />
            </div>
            <div className="field">
              <label htmlFor="currency">Default currency</label>
              <input
                id="currency"
                value={draft.defaultCurrency}
                onChange={(e) =>
                  setDraft((d) => (d ? { ...d, defaultCurrency: e.target.value } : d))
                }
              />
            </div>
            <div className="field">
              <label htmlFor="sub-amount">Subscription fee</label>
              <input
                id="sub-amount"
                type="number"
                step="0.01"
                value={draft.cursorSubscriptionAmount}
                onChange={(e) =>
                  setDraft((d) =>
                    d ? { ...d, cursorSubscriptionAmount: Number(e.target.value) } : d,
                  )
                }
              />
            </div>
            <div className="field">
              <label htmlFor="sub-currency">Subscription currency</label>
              <input
                id="sub-currency"
                value={draft.cursorSubscriptionCurrency}
                onChange={(e) =>
                  setDraft((d) =>
                    d ? { ...d, cursorSubscriptionCurrency: e.target.value } : d,
                  )
                }
              />
            </div>
            <div className="field">
              <label htmlFor="alloc-method">Allocation method</label>
              <select
                id="alloc-method"
                value={draft.cursorAllocationMethod}
                onChange={(e) =>
                  setDraft((d) =>
                    d ? { ...d, cursorAllocationMethod: e.target.value } : d,
                  )
                }
              >
                {ALLOCATION_METHODS.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="retention">Data retention (days)</label>
              <input
                id="retention"
                type="number"
                min={0}
                placeholder="Unlimited"
                value={draft.dataRetentionDays ?? ''}
                onChange={(e) =>
                  setDraft((d) =>
                    d
                      ? {
                          ...d,
                          dataRetentionDays: e.target.value === '' ? null : Number(e.target.value),
                        }
                      : d,
                  )
                }
              />
            </div>
          </div>

          <div className="field">
            <label htmlFor="export-path">Export path</label>
            <input
              id="export-path"
              className="mono"
              value={draft.exportPath}
              onChange={(e) => setDraft((d) => (d ? { ...d, exportPath: e.target.value } : d))}
            />
          </div>

          <label className="row">
            <input
              type="checkbox"
              checked={draft.autoCreateProjects}
              onChange={(e) =>
                setDraft((d) => (d ? { ...d, autoCreateProjects: e.target.checked } : d))
              }
            />
            Auto-create projects for unknown repositories
          </label>

          <label className="row">
            <input
              type="checkbox"
              checked={draft.enablePromptHashing}
              onChange={(e) =>
                setDraft((d) => (d ? { ...d, enablePromptHashing: e.target.checked } : d))
              }
            />
            Enable salted prompt hashing
          </label>

          <label className="row">
            <input
              type="checkbox"
              checked={draft.storePromptContent}
              onChange={(e) =>
                setDraft((d) => (d ? { ...d, storePromptContent: e.target.checked } : d))
              }
            />
            Store prompt content (encrypted at rest)
          </label>

          <label className="row">
            <input
              type="checkbox"
              checked={draft.storeResponseContent}
              onChange={(e) =>
                setDraft((d) => (d ? { ...d, storeResponseContent: e.target.checked } : d))
              }
            />
            Store response content (encrypted at rest)
          </label>

          {contentWarning ? (
            <div className="warning-banner" role="alert">
              WARNING: Content storage is enabled. Prompt and/or response text may be persisted.
              Prefer hashing-only unless you require full content for audits.
            </div>
          ) : null}

          <div className="row">
            <button type="submit" className="btn" disabled={updateSettings.isPending}>
              Save settings
            </button>
            {message ? <span>{message}</span> : null}
          </div>
        </form>
      </section>

      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>API key management</h2>
            <p>
              Dashboard auth uses localStorage key <span className="mono">{API_KEY_STORAGE}</span>.
            </p>
          </div>
        </div>

        <div className="panel stack">
          <div className="field-row">
            <div className="field">
              <label htmlFor="local-api-key">Bearer key for this browser</label>
              <input
                id="local-api-key"
                className="mono"
                type="password"
                value={localKey}
                onChange={(e) => setLocalKey(e.target.value)}
                autoComplete="off"
              />
            </div>
          </div>
          <div className="row">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => {
                setStoredApiKey(localKey.trim() || null);
                setMessage('Local API key saved. Reloading…');
                window.location.reload();
              }}
            >
              Save local key
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => {
                setLocalKey('');
                setStoredApiKey(null);
                setMessage('Local API key cleared.');
              }}
            >
              Clear local key
            </button>
          </div>

          <div className="field-row">
            <div className="field">
              <label htmlFor="new-key-name">Create server API key</label>
              <input
                id="new-key-name"
                value={newKeyName}
                onChange={(e) => setNewKeyName(e.target.value)}
              />
            </div>
          </div>
          <div className="row">
            <button
              type="button"
              className="btn"
              disabled={createKey.isPending || !newKeyName.trim()}
              onClick={() =>
                createKey.mutate(
                  { name: newKeyName.trim() },
                  {
                    onSuccess: (result) => {
                      setCreatedPlaintext(result.apiKey);
                      setLocalKey(result.apiKey);
                      setStoredApiKey(result.apiKey);
                    },
                  },
                )
              }
            >
              Create via API
            </button>
          </div>
          {createdPlaintext ? (
            <div className="warning-banner" role="status">
              Copy this key now — it is shown only once:
              <div className="mono" style={{ marginTop: '0.5rem', wordBreak: 'break-all' }}>
                {createdPlaintext}
              </div>
            </div>
          ) : null}

          {apiKeys.isError ? (
            <ErrorState
              message={
                apiKeys.error instanceof Error ? apiKeys.error.message : 'Unable to list API keys'
              }
            />
          ) : (
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Created</th>
                    <th>Last used</th>
                    <th>Expires</th>
                    <th>Status</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {(apiKeys.data ?? []).map((key) => (
                    <tr key={key.id}>
                      <td>{key.name}</td>
                      <td>{formatDateTime(key.createdAtUtc)}</td>
                      <td>{formatDateTime(key.lastUsedAtUtc)}</td>
                      <td>{formatDateTime(key.expiresAtUtc)}</td>
                      <td>
                        <StatusBadge
                          label={key.isActive ? 'Active' : 'Revoked'}
                          tone={key.isActive ? 'success' : 'danger'}
                        />
                      </td>
                      <td>
                        {key.isActive ? (
                          <button
                            type="button"
                            className="btn btn-danger"
                            onClick={() => revokeKey.mutate(key.id)}
                          >
                            Revoke
                          </button>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>

      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>Database & integrations</h2>
            <p>Local status for storage and editor integrations.</p>
          </div>
        </div>
        <div className="panel stack">
          <div className="field-row">
            <div>
              <div className="label">Database</div>
              <strong className="mono">{status.data?.databasePath ?? settings.data?.databasePath}</strong>
            </div>
            <div>
              <div className="label">Provider</div>
              <strong>{status.data?.databaseProvider ?? settings.data?.databaseProvider}</strong>
            </div>
            <div>
              <div className="label">DB health</div>
              <StatusBadge
                label={status.data?.isHealthy ? 'OK' : 'Check required'}
                tone={status.data?.isHealthy ? 'success' : 'warning'}
              />
            </div>
          </div>

          <div className="field-row">
            <div>
              <div className="label">Cursor hooks</div>
              <StatusBadge
                label={integrations.data?.cursorHooksConfigured ? 'Configured' : 'Unknown / not detected'}
                tone={integrations.data?.cursorHooksConfigured ? 'success' : 'warning'}
              />
            </div>
            <div>
              <div className="label">VS Code extension</div>
              <StatusBadge
                label={integrations.data?.vscodeExtensionDetected ? 'Detected' : 'Unknown / not detected'}
                tone={integrations.data?.vscodeExtensionDetected ? 'success' : 'warning'}
              />
            </div>
            <div>
              <div className="label">MCP</div>
              <StatusBadge
                label={integrations.data?.mcpConfigured ? 'Configured' : 'Unknown / not detected'}
                tone={integrations.data?.mcpConfigured ? 'success' : 'warning'}
              />
            </div>
          </div>
          {integrations.data?.notes?.length ? (
            <ul>
              {integrations.data.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          ) : null}
        </div>
      </section>
    </Page>
  );
}
