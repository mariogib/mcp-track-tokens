import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  API_KEY_STORAGE,
  ApiError,
  api,
  getStoredApiKey,
  setStoredApiKey,
} from '../api/client';
import {
  useApiKeysQuery,
  useCreateApiKeyMutation,
  useCreateTimesheetCategoryMutation,
  useDatabaseBackupInfoQuery,
  useDeleteTimesheetCategoryMutation,
  useCheckCursorHooksMutation,
  useIntegrationsQuery,
  useReplayOfflineQueueMutation,
  useRestoreDatabaseUploadMutation,
  useRevokeApiKeyMutation,
  useSettingsQuery,
  useStatusQuery,
  useTimesheetCategoriesQuery,
  useUpdateSettingsMutation,
  useFetchCursorTokenRatesMutation,
  useUpdateTimesheetCategoryMutation,
} from '../api/hooks';
import {
  bearerKeyGateMessage,
  useStoredApiKey,
  type ApiKeyGateLocationState,
  type ApiKeyGateReason,
} from '../hooks/useApiKeyAccess';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import type {
  CursorModelTokenRateDto,
  SettingsDto,
  TimesheetCategoryDto,
  UpdateSettingsRequest,
} from '../api/types';
import { ErrorState, LoadingState } from '../components/States';
import { Panel } from '../components/MetricCard';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
import { PopupForm, TextLink, ThemeButton } from '../shared/adminUi';
import {
  deleteLocalBackupFile,
  getStoredBackupFolder,
  listLocalBackupFiles,
  pickBackupFolder,
  readLocalBackupFile,
  resolveLastBackupFolder,
  saveBackupToFolder,
  type BackupFolderRef,
  type LocalBackupFile,
} from '../utils/backupFolder';
import { formatDateTime } from '../utils/format';

type HelpContent = string | { summary: string; detail: string };

function resolveHelp(help: HelpContent): { summary: string; detail: string } {
  if (typeof help === 'string') {
    const trimmed = help.trim();
    const sentenceEnd = trimmed.search(/[.!?]\s/);
    const summary =
      sentenceEnd > 0 && sentenceEnd < 120
        ? trimmed.slice(0, sentenceEnd + 1)
        : trimmed.length > 110
          ? `${trimmed.slice(0, 107).trimEnd()}…`
          : trimmed;
    return { summary, detail: trimmed };
  }
  return help;
}

/** `?` control: CSS popup via `data-tooltip` (no native `title` tooltip). */
function SettingHelp({
  help,
  align = 'start',
}: {
  help: HelpContent;
  align?: 'center' | 'start' | 'end';
}) {
  const { detail } = resolveHelp(help);
  const alignClass =
    align === 'center'
      ? ' setting-help--align-center'
      : align === 'end'
        ? ' setting-help--align-end'
        : ' setting-help--align-start';
  return (
    <span
      className={`setting-help${alignClass}`}
      data-tooltip={detail}
      tabIndex={0}
      role="img"
      aria-label={detail}
    >
      ?
    </span>
  );
}

function SettingLabel({
  htmlFor,
  help,
  children,
}: {
  htmlFor?: string;
  help: HelpContent;
  children: ReactNode;
}) {
  return (
    <label htmlFor={htmlFor} className="setting-label">
      <span>{children}</span>
      <SettingHelp help={help} />
    </label>
  );
}

function SettingCheck({
  help,
  checked,
  onChange,
  children,
}: {
  help: HelpContent;
  checked: boolean;
  onChange: (checked: boolean) => void;
  children: ReactNode;
}) {
  return (
    <label className="row setting-label--row">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span>{children}</span>
      <SettingHelp help={help} />
    </label>
  );
}

function CursorCostHelpDialog({ onClose }: { onClose: () => void }) {
  return (
    <PopupForm
      title="How Cursor calculates cost"
      subtitle="Based on Cursor’s Models & Pricing for individual and team plans."
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Close
        </button>
      }
    >
      <div className="stack help-dialog-body">
          <section>
            <h3>Per-token formula</h3>
            <p>
              Model prices are listed <strong>per 1,000,000 tokens</strong>. For a request, Cursor
              multiplies each token type by its rate and sums them:
            </p>
            <pre className="help-formula mono">
              {`cost ≈
  (inputTokens        / 1M) × inputRate
+ (cacheWriteTokens   / 1M) × cacheWriteRate
+ (cacheReadTokens    / 1M) × cacheReadRate
+ (outputTokens       / 1M) × outputRate
(+ reasoningTokens    / 1M) × reasoningRate   // when billed separately`}
            </pre>
            <ul>
              <li>
                <strong>Input</strong> — prompt and context sent to the model (message, files,
                history).
              </li>
              <li>
                <strong>Cache write</strong> — new context written into the provider prompt cache.
              </li>
              <li>
                <strong>Cache read</strong> — reused cached context (usually cheaper than input).
              </li>
              <li>
                <strong>Output</strong> — generated reply text streamed back to you.
              </li>
              <li>
                <strong>Reasoning</strong> — internal “thinking” tokens some models bill separately
                from the visible reply.
              </li>
            </ul>
          </section>

          <section>
            <h3>Usage pools (individual plans)</h3>
            <p>Cursor tracks two monthly pools that reset with your billing cycle:</p>
            <ul>
              <li>
                <strong>Cursor Models</strong> — included usage for first-party models such as
                Composer and Cursor Grok.
              </li>
              <li>
                <strong>Other Models</strong> — third-party models charged at that model’s API
                price. Plans include a dollar allowance (for example $20 on Pro); beyond that you
                can pay on-demand at the same rates or upgrade.
              </li>
            </ul>
            <p>
              Choosing a more expensive model consumes the Other Models pool faster. Tab
              completions are unlimited on individual plans and are not billed from these pools.
            </p>
          </section>

          <section>
            <h3>Auto modes</h3>
            <ul>
              <li>
                <strong>Auto Cost</strong> — billed at fixed Auto Cost rates per million tokens,
                regardless of which underlying model runs the request.
              </li>
              <li>
                <strong>Auto Balance / Auto Intelligence</strong> — billed at the API rates of the
                model actually used (from the pricing table).
              </li>
            </ul>
          </section>

          <section>
            <h3>Teams &amp; Enterprise extras</h3>
            <ul>
              <li>
                Third-party model requests may add a <strong>Cursor Token Rate</strong> (currently
                $0.25 per million tokens) on top of model API pricing for included, on-demand, and
                BYOK usage. First-party Cursor models and Auto Cost are exempt.
              </li>
              <li>
                Regional data residency can add a surcharge (for example 10%) on eligible model
                pricing.
              </li>
              <li>
                Legacy request-based plans may still use Max Mode (API rate + 20%) for extended
                context.
              </li>
            </ul>
          </section>

          <section>
            <h3>Included vs on-demand</h3>
            <p>
              Usage inside your monthly allowance is “included.” Cursor usage CSV/JSON exports often
              show <strong>$0</strong> for Included / Free rows even when tokens were consumed. After
              you exceed included usage, on-demand spend continues at the same API rates and appears
              as non-zero cost in exports.
            </p>
          </section>

          <section>
            <h3>How this rate card relates</h3>
            <p>
              This table mirrors Cursor’s per-million rates so MCP Track Tokens can estimate spend
              when imported cost is $0. Calculated cost uses the same style of formula (token counts
              × rates). Enable{' '}
              <em>Estimate usage cost from these rates when imported cost is zero</em> to apply it in
              dashboards and reports. Use <strong>Get Rates</strong> to refresh from{' '}
              <TextLink href="https://cursor.com/docs/models-and-pricing" external>
                cursor.com/docs/models-and-pricing
              </TextLink>
              .
            </p>
          </section>
        </div>
    </PopupForm>
  );
}

const ALLOCATION_METHODS = [
  'NotAllocated',
  'EqualAcrossActiveProjects',
  'ByActiveProjectTime',
  'ByPromptCount',
  'ByAgentDuration',
];

const SETTINGS_TABS = [
  'Connection',
  'Display',
  'Tracking',
  'Cursor token costs',
  'API keys',
  'Data',
  'Backup & restore',
  'Integrations',
] as const;

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

const DEFAULT_TOKEN_RATES: CursorModelTokenRateDto[] = [
  {
    model: 'Auto',
    inputPerMillion: 1.25,
    outputPerMillion: 6,
    cacheReadPerMillion: 0.25,
    cacheWritePerMillion: 1.25,
  },
  {
    model: '*',
    inputPerMillion: 1.25,
    outputPerMillion: 6,
    cacheReadPerMillion: 0.25,
    cacheWritePerMillion: 1.25,
  },
];

type SettingsDraft = UpdateSettingsRequest & {
  inactivityThresholdMinutes: number;
  sessionInactivityCloseMinutes: number;
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
  estimateCostFromTokenRates: boolean;
  cursorTokenRates: CursorModelTokenRateDto[];
};

function toDraft(settings: SettingsDto): SettingsDraft {
  return {
    inactivityThresholdMinutes: settings.inactivityThresholdMinutes,
    sessionInactivityCloseMinutes: settings.sessionInactivityCloseMinutes ?? 60,
    defaultCurrency: settings.defaultCurrency,
    cursorSubscriptionAmount: settings.cursorSubscriptionAmount,
    cursorSubscriptionCurrency: settings.cursorSubscriptionCurrency,
    cursorAllocationMethod: settings.cursorAllocationMethod,
    storePromptContent: settings.storePromptContent,
    storeResponseContent: settings.storeResponseContent,
    enablePromptHashing: settings.enablePromptHashing,
    exportPath: settings.exportPath,
    dataRetentionDays: settings.dataRetentionDays ?? null,
    autoCreateProjects: settings.autoCreateProjects ?? true,
    estimateCostFromTokenRates: settings.estimateCostFromTokenRates ?? false,
    cursorTokenRates:
      settings.cursorTokenRates && settings.cursorTokenRates.length > 0
        ? settings.cursorTokenRates.map((r) => ({ ...r }))
        : DEFAULT_TOKEN_RATES.map((r) => ({ ...r })),
  };
}

function emptyRate(): CursorModelTokenRateDto {
  return {
    model: 'new-model',
    inputPerMillion: 1.25,
    outputPerMillion: 6,
    cacheReadPerMillion: 0.25,
    cacheWritePerMillion: 1.25,
    reasoningPerMillion: null,
  };
}

export function SettingsPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const storedApiKey = useStoredApiKey();
  const settings = useSettingsQuery();
  const status = useStatusQuery();
  const apiKeys = useApiKeysQuery();
  const timesheetCategories = useTimesheetCategoriesQuery();
  const integrations = useIntegrationsQuery();
  const checkCursorHooks = useCheckCursorHooksMutation();
  const replayQueue = useReplayOfflineQueueMutation();
  const updateSettings = useUpdateSettingsMutation();
  const fetchCursorRates = useFetchCursorTokenRatesMutation();
  const createKey = useCreateApiKeyMutation();
  const revokeKey = useRevokeApiKeyMutation();
  const createCategory = useCreateTimesheetCategoryMutation();
  const updateCategory = useUpdateTimesheetCategoryMutation();
  const deleteCategory = useDeleteTimesheetCategoryMutation();
  const restoreUpload = useRestoreDatabaseUploadMutation();

  const keyValidation = useQuery({
    queryKey: ['api-key-validation', storedApiKey ?? ''],
    queryFn: ({ signal }) => api.status(signal),
    enabled: Boolean(storedApiKey),
    retry: false,
    staleTime: 30_000,
  });

  // Only trust live key state — do not keep showing the redirect reason while a
  // newly saved key is still validating (history.state survives reload).
  const bearerKeyIssue: ApiKeyGateReason | null = !storedApiKey
    ? 'missing'
    : keyValidation.isError &&
        keyValidation.error instanceof ApiError &&
        keyValidation.error.status === 401
      ? 'invalid'
      : null;

  const wasRedirectedForKey =
    (location.state as ApiKeyGateLocationState | null)?.apiKeyGate === bearerKeyIssue;

  const bearerKeyGateBanner = bearerKeyIssue ? (
    <div className="error-box" role="alert">
      <p>
        {wasRedirectedForKey
          ? bearerKeyGateMessage(bearerKeyIssue)
          : bearerKeyIssue === 'missing'
            ? 'No Bearer API key is saved in this browser. Paste a valid key under Local connection and click Save local key to use the rest of the dashboard.'
            : 'The saved Bearer API key was rejected by the API (401 Unauthorized). Replace it with a valid key under Local connection and click Save local key.'}
      </p>
    </div>
  ) : null;

  useEffect(() => {
    if (!storedApiKey || !keyValidation.isSuccess) {
      return;
    }
    if (!(location.state as ApiKeyGateLocationState | null)?.apiKeyGate) {
      return;
    }
    navigate(`${location.pathname}${location.search}`, { replace: true, state: {} });
  }, [
    keyValidation.isSuccess,
    location.pathname,
    location.search,
    location.state,
    navigate,
    storedApiKey,
  ]);

  const clearApiKeyGateState = () => {
    if (!(location.state as ApiKeyGateLocationState | null)?.apiKeyGate) {
      return;
    }
    navigate(`${location.pathname}${location.search}`, { replace: true, state: {} });
  };

  const [tab, setTab] = useTabSearchParam(SETTINGS_TABS, 'Connection');
  const [draft, setDraft] = useState<SettingsDraft | null>(null);
  const [localKey, setLocalKey] = useState(() => getStoredApiKey() ?? '');
  const [newKeyName, setNewKeyName] = useState('Dashboard');
  const [newCategoryName, setNewCategoryName] = useState('');
  const [categoryCreateOpen, setCategoryCreateOpen] = useState(false);
  const [apiKeyCreateOpen, setApiKeyCreateOpen] = useState(false);
  const [editingCategoryId, setEditingCategoryId] = useState<string | null>(null);
  const [categoryDraft, setCategoryDraft] = useState({ name: '', sortOrder: 0, isActive: true });
  const [categoryMessage, setCategoryMessage] = useState<string | null>(null);
  const [categoryStatusFilter, setCategoryStatusFilter] = useState('');
  const [apiKeyStatusFilter, setApiKeyStatusFilter] = useState('');
  const [createdPlaintext, setCreatedPlaintext] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [backupFolderLabel, setBackupFolderLabel] = useState(
    () => getStoredBackupFolder() ?? '',
  );
  const [selectedFolder, setSelectedFolder] = useState<BackupFolderRef | null>(null);
  const [localBackups, setLocalBackups] = useState<LocalBackupFile[]>([]);
  const [backupBusy, setBackupBusy] = useState(false);
  const [backupMessage, setBackupMessage] = useState<string | null>(null);
  const [backupListReady, setBackupListReady] = useState(false);
  const [showCursorCostHelp, setShowCursorCostHelp] = useState(false);

  const backupInfo = useDatabaseBackupInfoQuery(undefined, true);

  const filteredCategories = useMemo(() => {
    const list = timesheetCategories.data ?? [];
    if (!categoryStatusFilter) {
      return list;
    }
    return list.filter((category) =>
      categoryStatusFilter === 'active' ? category.isActive : !category.isActive,
    );
  }, [categoryStatusFilter, timesheetCategories.data]);

  const filteredApiKeys = useMemo(() => {
    const list = apiKeys.data ?? [];
    if (!apiKeyStatusFilter) {
      return list;
    }
    return list.filter((key) =>
      apiKeyStatusFilter === 'active' ? key.isActive : !key.isActive,
    );
  }, [apiKeyStatusFilter, apiKeys.data]);

  useEffect(() => {
    if (settings.data) {
      setDraft(toDraft(settings.data));
    }
  }, [settings.data]);

  // Load the last backup folder (or desktop default Documents\MCP Track Tokens) on first open.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const folder = await resolveLastBackupFolder({
          serverDefaultPath: backupInfo.data?.defaultFolder,
        });
        if (cancelled || !folder) {
          if (!cancelled) {
            setBackupListReady(true);
          }
          return;
        }

        const label = folder.path ?? getStoredBackupFolder() ?? 'MCP Track Tokens';
        const files = await listLocalBackupFiles(folder);
        if (cancelled) {
          return;
        }

        setSelectedFolder(folder);
        setBackupFolderLabel(label);
        setLocalBackups(files);
      } catch {
        /* keep empty list until the user picks a folder */
      } finally {
        if (!cancelled) {
          setBackupListReady(true);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [backupInfo.data?.defaultFolder]);

  const saveDraft = (next?: SettingsDraft) => {
    const payload = next ?? draft;
    if (!payload) {
      return;
    }

    setMessage(null);
    updateSettings.mutate(
      {
        ...payload,
        clearDataRetentionDays: payload.dataRetentionDays == null,
      },
      {
        onSuccess: () => setMessage('Settings saved.'),
        onError: (err) =>
          setMessage(err instanceof Error ? err.message : 'Failed to save settings'),
      },
    );
  };

  const updateRate = (
    index: number,
    patch: Partial<CursorModelTokenRateDto>,
  ) => {
    setDraft((d) => {
      if (!d) {
        return d;
      }

      const rates = d.cursorTokenRates.map((rate, i) =>
        i === index ? { ...rate, ...patch } : rate,
      );
      return { ...d, cursorTokenRates: rates };
    });
  };

  const connectionPanel = (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>Local connection</h2>
          <p>
            Dashboard auth uses localStorage key <span className="mono">{API_KEY_STORAGE}</span>.
            Other pages redirect here when this Bearer key is missing or rejected by the API.
            Paste a valid key, then save, before using Overview and the rest of the app.
          </p>
        </div>
      </div>

      <Panel className="stack">
        <div className="field-row">
          <div className="field">
            <SettingLabel
              htmlFor="local-api-key"
              help={{
                summary: 'Bearer API key stored in this browser for dashboard requests.',
                detail:
                  'Saved in this browser’s localStorage and sent as Authorization: Bearer on API calls. Create a server key under API keys (no existing Bearer required), then paste it here. Clearing the local key does not revoke the server key. Until a valid key is saved, most Settings tabs cannot load.',
              }}
            >
              Bearer key for this browser
            </SettingLabel>
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
              const nextKey = localKey.trim() || null;
              setStoredApiKey(nextKey);
              setMessage('Local API key saved. Reloading…');
              // assign (not reload) so redirect gate state is dropped from history
              window.location.assign(`${location.pathname}${location.search}`);
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
          {bearerKeyIssue ? (
            <button
              type="button"
              className="btn"
              onClick={() => {
                setTab('API keys');
                setNewKeyName('Dashboard');
                setApiKeyCreateOpen(true);
              }}
            >
              Create server API key
            </button>
          ) : null}
        </div>
        {message && tab === 'Connection' ? <p>{message}</p> : null}
        {createdPlaintext ? (
          <div className="warning-banner" role="status">
            New key (copy now): <code className="mono">{createdPlaintext}</code>
          </div>
        ) : null}
      </Panel>
    </section>
  );

  const displayPanel = (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>Display</h2>
          <p>Choose the look-and-feel and colour palette for this dashboard.</p>
        </div>
      </div>

      <Panel className="stack">
        <div className="field">
          <SettingLabel
            help={{
              summary: 'Theme and look-and-feel applied across the dashboard.',
              detail:
                'Pick a LunarQ or Microsoft Fluent preset. The choice is stored in this browser and applied on every visit. Fluent presets keep Fluent density and type; LunarQ presets keep the classic LunarQ chrome.',
            }}
          >
            Theme
          </SettingLabel>
          <div className="settings-theme-picker">
            <ThemeButton />
          </div>
        </div>
      </Panel>
    </section>
  );

  const apiKeysPanel = (
    <section className="page-section">
      <div className="section-header">
        <div>
          <h2>API key management</h2>
          <p>
            Create and revoke server API keys. Creating a key does not require a Bearer token, so you
            can recover if the local key is missing or invalid. Revoked keys can be permanently
            deleted from the list. The browser still uses the localStorage key under Connection.
          </p>
        </div>
        <button
          type="button"
          className="btn"
          onClick={() => {
            setNewKeyName('Dashboard');
            setApiKeyCreateOpen(true);
          }}
        >
          Create API key
        </button>
      </div>

      <Panel className="stack">
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
              apiKeys.error instanceof Error
                ? apiKeys.error.message
                : 'Unable to list API keys'
            }
          />
        ) : (
          <div className="stack">
            <div className="field" style={{ maxWidth: '14rem' }}>
              <label htmlFor="api-key-status-filter">Status</label>
              <select
                id="api-key-status-filter"
                value={apiKeyStatusFilter}
                onChange={(e) => setApiKeyStatusFilter(e.target.value)}
              >
                <option value="">All statuses</option>
                <option value="active">Active</option>
                <option value="revoked">Revoked</option>
              </select>
            </div>
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
                  {filteredApiKeys.map((key) => (
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
                            title="Revoke this key so it can no longer authenticate"
                          >
                            Revoke
                          </button>
                        ) : (
                          <button
                            type="button"
                            className="btn btn-danger"
                            onClick={() => {
                              if (
                                window.confirm(
                                  `Permanently delete revoked key “${key.name}”? This cannot be undone.`,
                                )
                              ) {
                                revokeKey.mutate(key.id);
                              }
                            }}
                            title="Permanently remove this revoked key from the list"
                          >
                            Delete
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Panel>

      {apiKeyCreateOpen ? (
        <PopupForm
          title="Create API key"
          contentClassName="popup-form--narrow"
          onClose={() => setApiKeyCreateOpen(false)}
          onSubmit={(e) => {
            const event = e as FormEvent;
            event.preventDefault();
            if (!newKeyName.trim()) {
              return;
            }
            createKey.mutate(
              { name: newKeyName.trim() },
              {
                onSuccess: (result) => {
                  setCreatedPlaintext(result.apiKey);
                  setLocalKey(result.apiKey);
                  setStoredApiKey(result.apiKey);
                  clearApiKeyGateState();
                  void queryClient.invalidateQueries({ queryKey: ['api-key-validation'] });
                  void queryClient.invalidateQueries({ queryKey: ['api-keys'] });
                  void queryClient.invalidateQueries({ queryKey: ['settings'] });
                  setApiKeyCreateOpen(false);
                  setMessage('New API key saved as the local Bearer key.');
                },
              },
            );
          }}
          footer={
            <>
              <button
                type="submit"
                className="btn"
                disabled={createKey.isPending || !newKeyName.trim()}
                title="Create a new server API key with the name above. The plaintext secret is shown only once."
              >
                {createKey.isPending ? 'Creating…' : 'Create API key'}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setApiKeyCreateOpen(false)}
              >
                Cancel
              </button>
            </>
          }
        >
          <div className="field">
            <SettingLabel
              htmlFor="new-key-name"
              help={{
                summary: 'Friendly name for a new server API key.',
                detail:
                  'Label only—used to identify the key in the list. After creation the plaintext secret is shown once; copy it into Bearer key for this browser or your client. Creating a key does not require an existing Bearer token. Revoking a key invalidates it immediately; clearing the local browser key does not revoke the server key.',
              }}
            >
              Name
            </SettingLabel>
            <input
              id="new-key-name"
              required
              value={newKeyName}
              onChange={(e) => setNewKeyName(e.target.value)}
            />
          </div>
        </PopupForm>
      ) : null}
    </section>
  );

  const settingsTabs = (
    <div className="tabs" role="tablist" aria-label="Settings sections">
      {SETTINGS_TABS.map((name) => (
        <button
          key={name}
          type="button"
          role="tab"
          aria-selected={tab === name}
          className={`tab${tab === name ? ' active' : ''}`}
          onClick={() => {
            setTab(name);
            setMessage(null);
          }}
        >
          {name}
        </button>
      ))}
    </div>
  );

  if (!draft) {
    return (
      <Page>
        {bearerKeyGateBanner}
        {settingsTabs}
        {tab === 'Connection' && connectionPanel}
        {tab === 'Display' && displayPanel}
        {tab === 'API keys' && apiKeysPanel}
        {tab !== 'Connection' && tab !== 'Display' && tab !== 'API keys' ? (
          settings.isError ? (
            <ErrorState
              message={
                settings.error instanceof Error ? settings.error.message : 'Failed to load settings'
              }
              error={settings.error}
            />
          ) : (
            <LoadingState label="Loading settings…" />
          )
        ) : null}
      </Page>
    );
  }

  const contentWarning = draft.storePromptContent || draft.storeResponseContent;

  return (
    <Page>
      {bearerKeyGateBanner}
      {settingsTabs}

      {message && tab !== 'Connection' ? (
        <p className="muted" style={{ marginTop: '0.75rem' }}>
          {message}
        </p>
      ) : null}

      {tab === 'Connection' && connectionPanel}

      {tab === 'Display' && displayPanel}

      {tab === 'Tracking' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Tracking preferences</h2>
              <p>Inactivity, currency, subscription allocation, and retention.</p>
            </div>
          </div>

          <Panel className="stack"><form
            className="stack"
            onSubmit={(e) => {
              e.preventDefault();
              saveDraft();
            }}
          >
            <div className="field-row">
              <div className="field">
                <SettingLabel
                  htmlFor="inactivity"
                  help={{
                    summary: 'Idle gap (minutes) used to split active project time windows.',
                    detail:
                      'When calculating active project time, prompts and agent events that fall within this many minutes of each other stay in the same activity window. A longer quiet gap starts a new window. This does not end editor sessions—use Session close after idle for that.',
                  }}
                >
                  Inactivity threshold (minutes)
                </SettingLabel>
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
                <SettingLabel
                  htmlFor="session-close"
                  help={{
                    summary: 'Close an open editor session after this many idle minutes.',
                    detail:
                      'If an open editor session for a workspace has had no prompt for longer than this, the next prompt ends that session at the last prompt timestamp and starts a new session. Prevents one long-lived session from spanning unrelated work days.',
                  }}
                >
                  Session close after idle (minutes)
                </SettingLabel>
                <input
                  id="session-close"
                  type="number"
                  min={1}
                  value={draft.sessionInactivityCloseMinutes}
                  onChange={(e) =>
                    setDraft((d) =>
                      d ? { ...d, sessionInactivityCloseMinutes: Number(e.target.value) } : d,
                    )
                  }
                />
              </div>
              <div className="field">
                <SettingLabel
                  htmlFor="currency"
                  help={{
                    summary: 'Default ISO currency for projects and cost displays.',
                    detail:
                      'Used for new projects and cost formatting when a project does not set its own currency. Prefer a three-letter ISO code such as USD or EUR that matches how you bill clients.',
                  }}
                >
                  Default currency
                </SettingLabel>
                <input
                  id="currency"
                  value={draft.defaultCurrency}
                  onChange={(e) =>
                    setDraft((d) => (d ? { ...d, defaultCurrency: e.target.value } : d))
                  }
                />
              </div>
              <div className="field">
                <SettingLabel
                  htmlFor="sub-amount"
                  help={{
                    summary: 'Monthly Cursor subscription fee to allocate across projects.',
                    detail:
                      'Fixed subscription amount (not usage-based on-demand spend). Combined with the allocation method below to share that fee across projects in cost reports. Set to 0 if you only track usage-based cost.',
                  }}
                >
                  Subscription fee
                </SettingLabel>
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
                <SettingLabel
                  htmlFor="sub-currency"
                  help={{
                    summary: 'Currency for the subscription fee amount.',
                    detail:
                      'Usually the same currency Cursor bills you in. Displayed with the subscription fee in allocation and billing reports; independent of each project’s default currency when they differ.',
                  }}
                >
                  Subscription currency
                </SettingLabel>
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
                <SettingLabel
                  htmlFor="alloc-method"
                  help={{
                    summary: 'How the subscription fee is split across projects.',
                    detail:
                      'Chooses the weight used when sharing the monthly subscription fee—for example by active project time or prompt count. NotAllocated skips subscription sharing so only usage-based costs appear in project totals.',
                  }}
                >
                  Allocation method
                </SettingLabel>
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
                <SettingLabel
                  htmlFor="retention"
                  help={{
                    summary: 'Optional automatic purge horizon in days.',
                    detail:
                      'When set, older tracking data past this many days can be purged on a schedule. Leave empty for unlimited retention—nothing is deleted automatically. Choose a value that matches your audit and storage needs.',
                  }}
                >
                  Data retention (days)
                </SettingLabel>
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
                            dataRetentionDays:
                              e.target.value === '' ? null : Number(e.target.value),
                          }
                        : d,
                    )
                  }
                />
              </div>
            </div>

            <div className="field">
              <SettingLabel
                htmlFor="export-path"
                help={{
                  summary: 'Server directory for CSV/JSON export files.',
                  detail:
                    'Absolute path on the tracking host where export jobs write files. Must be an approved export path configured for the server; relative or disallowed paths are rejected for security.',
                }}
              >
                Export path
              </SettingLabel>
              <input
                id="export-path"
                className="mono"
                value={draft.exportPath}
                onChange={(e) => setDraft((d) => (d ? { ...d, exportPath: e.target.value } : d))}
              />
            </div>

            <SettingCheck
              help={{
                summary: 'Auto-create a project when an event’s repository is unknown.',
                detail:
                  'If ingest cannot match a workspace to an existing project, a new project is created from the repository identity instead of leaving the event unallocated. Turn off if you prefer to review and assign unknown repos manually.',
              }}
              checked={draft.autoCreateProjects}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, autoCreateProjects: checked } : d))
              }
            >
              Auto-create projects for unknown repositories
            </SettingCheck>

            <SettingCheck
              help={{
                summary: 'Store a salted hash of prompt text for duplicate detection.',
                detail:
                  'Keeps a one-way hash of prompt text so duplicates can be detected without storing the raw prompt. Recommended when Store prompt content is off. Hashing alone cannot reconstruct the original text.',
              }}
              checked={draft.enablePromptHashing}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, enablePromptHashing: checked } : d))
              }
            >
              Enable salted prompt hashing
            </SettingCheck>

            <SettingCheck
              help={{
                summary: 'Persist full prompt text encrypted at rest (privacy-sensitive).',
                detail:
                  'When on and encryption is configured, full prompt bodies are stored encrypted. Off by default. Editor hooks must also send content for anything to be saved. Prefer hashing-only unless you need full text for audits.',
              }}
              checked={draft.storePromptContent}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, storePromptContent: checked } : d))
              }
            >
              Store prompt content (encrypted at rest)
            </SettingCheck>

            <SettingCheck
              help={{
                summary: 'Persist agent response text encrypted at rest (privacy-sensitive).',
                detail:
                  'When on and encryption is configured, agent response bodies are stored encrypted. Off by default for privacy. Use only when you need response text for review or compliance; otherwise leave off and rely on token/cost metadata.',
              }}
              checked={draft.storeResponseContent}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, storeResponseContent: checked } : d))
              }
            >
              Store response content (encrypted at rest)
            </SettingCheck>

            {contentWarning ? (
              <div className="warning-banner" role="alert">
                WARNING: Content storage is enabled. Prompt and/or response text may be persisted.
                Prefer hashing-only unless you require full content for audits.
              </div>
            ) : null}

            <div className="row">
              <button
                type="submit"
                className="btn"
                disabled={updateSettings.isPending}
                title="Save tracking preferences to the database"
              >
                Save settings
              </button>
            </div>
          </form></Panel>
        </section>
      )}

      {tab === 'Cursor token costs' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Cursor token costs</h2>
              <p>
                Record rates in currency units per 1,000,000 tokens. Model names should match Cursor
                usage exports. Use <span className="mono">*</span> as the fallback when no model
                matches. Column order matches Cursor’s{' '}
                <TextLink href="https://cursor.com/docs/models-and-pricing" external>
                  Models &amp; Pricing
                </TextLink>{' '}
                (opens in a new tab).
              </p>
            </div>
          </div>

          <Panel className="stack">
            <SettingCheck
              help={{
                summary:
                  'When imported cost is $0 (Included/Free), estimate spend from this rate card.',
                detail:
                  'Cursor usage exports often report $0 for Included / Free usage even when tokens were consumed. When this option is on, the dashboard and reports estimate that spend by multiplying attributed token counts by the rates in this table (including Auto and * fallbacks). Turn it off to keep reported $0 as zero cost. Save rates after changing this flag.',
              }}
              checked={draft.estimateCostFromTokenRates}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, estimateCostFromTokenRates: checked } : d))
              }
            >
              Estimate usage cost from these rates when imported cost is zero (Included / Free)
            </SettingCheck>

            <div className="row">
              <button
                type="button"
                className="btn btn-secondary"
                title="Download model rates from cursor.com/docs/models-and-pricing and save them"
                disabled={fetchCursorRates.isPending || updateSettings.isPending}
                onClick={() => {
                  setMessage(null);
                  fetchCursorRates.mutate(undefined, {
                    onSuccess: (result) => {
                      setDraft((d) =>
                        d
                          ? {
                              ...d,
                              cursorTokenRates: result.rates.map((r) => ({
                                model: r.model,
                                inputPerMillion: r.inputPerMillion,
                                outputPerMillion: r.outputPerMillion,
                                cacheReadPerMillion: r.cacheReadPerMillion,
                                cacheWritePerMillion: r.cacheWritePerMillion,
                                reasoningPerMillion: r.reasoningPerMillion ?? null,
                              })),
                            }
                          : d,
                      );
                      const warningText =
                        result.warnings?.length > 0
                          ? ` Warnings: ${result.warnings.join(' ')}`
                          : '';
                      setMessage(
                        `Fetched and saved ${result.count} rates from Cursor docs.${warningText}`,
                      );
                    },
                    onError: (err) => {
                      setMessage(
                        err instanceof Error
                          ? err.message
                          : 'Failed to fetch Cursor pricing docs.',
                      );
                    },
                  });
                }}
              >
                {fetchCursorRates.isPending ? 'Getting rates…' : 'Get Rates'}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                title="Add a blank model rate row to the table"
                onClick={() =>
                  setDraft((d) =>
                    d
                      ? { ...d, cursorTokenRates: [...d.cursorTokenRates, emptyRate()] }
                      : d,
                  )
                }
              >
                Add model
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                title="Replace the table with the built-in Auto and * default rates"
                onClick={() =>
                  setDraft((d) =>
                    d
                      ? {
                          ...d,
                          cursorTokenRates: DEFAULT_TOKEN_RATES.map((r) => ({ ...r })),
                        }
                      : d,
                  )
                }
              >
                Reset defaults
              </button>
              <button
                type="button"
                className="btn"
                disabled={updateSettings.isPending}
                title="Save the rate card and estimate-cost flag to the database"
                onClick={() => saveDraft()}
              >
                Save rates
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setShowCursorCostHelp(true)}
              >
                Help
              </button>
            </div>

            {showCursorCostHelp ? (
              <CursorCostHelpDialog onClose={() => setShowCursorCostHelp(false)} />
            ) : null}

            {fetchCursorRates.isError ? (
              <ErrorState
                message={
                  fetchCursorRates.error instanceof Error
                    ? fetchCursorRates.error.message
                    : 'Failed to fetch Cursor pricing docs'
                }
              />
            ) : null}

            <div className="table-wrap">
              <table className="data token-rates">
                <colgroup>
                  <col className="token-rates-col-model" />
                  <col className="token-rates-col-rate" />
                  <col className="token-rates-col-rate" />
                  <col className="token-rates-col-rate" />
                  <col className="token-rates-col-rate" />
                  <col className="token-rates-col-rate" />
                  <col className="token-rates-col-actions" />
                </colgroup>
                <thead>
                  <tr>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Model{' '}
                        <SettingHelp
                          align="start"
                          help={{
                            summary: 'Model name from Cursor usage exports (* = fallback).',
                            detail:
                              'Enter the model string exactly as it appears in Cursor usage CSV/JSON exports (for example Auto, Grok 4.6, Composer 2.5, or a Claude/GPT SKU). Matching is case-insensitive after normalization. Use * as the catch-all rate when no other row matches. Get Rates pulls both the Cursor Models table (Grok/Composer) and the Other Models pricing table, and maps Auto Cost to Auto/* when present.',
                          }}
                        />
                      </span>
                    </th>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Input / 1M{' '}
                        <SettingHelp
                          align="start"
                          help={{
                            summary: 'Price per 1M input tokens.',
                            detail:
                              'Input tokens are the prompt and context the model reads before generating a reply (your message, attached files, and conversation history sent with the request). This rate is currency units per 1,000,000 of those tokens—same idea as Cursor’s Input column on Models & Pricing. Used when estimating Included/Free spend from token counts.',
                          }}
                        />
                      </span>
                    </th>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Cache write / 1M{' '}
                        <SettingHelp
                          help={{
                            summary: 'Price per 1M cache-write tokens.',
                            detail:
                              'Cache write tokens are billed when new prompt/context is written into the provider’s prompt cache for later reuse (first-time cache fill). Currency units per 1,000,000 of those tokens. Leave blank or 0 if you do not track cache writes; Get Rates fills this from Cursor docs when available.',
                          }}
                        />
                      </span>
                    </th>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Cache read / 1M{' '}
                        <SettingHelp
                          help={{
                            summary: 'Price per 1M cache-read tokens.',
                            detail:
                              'Cache read tokens are previously cached prompt/context that the model reuses instead of re-processing as fresh input. Usually cheaper than Input. Currency units per 1,000,000 of those tokens; used in calculated cost when usage rows include cached input volume.',
                          }}
                        />
                      </span>
                    </th>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Output / 1M{' '}
                        <SettingHelp
                          help={{
                            summary: 'Price per 1M output tokens.',
                            detail:
                              'Output tokens are the model’s generated reply text (completions streamed back to you). Currency units per 1,000,000 of those tokens—same idea as Cursor’s Output column. Combined with input and cache rates when estimating cost from token counts.',
                          }}
                        />
                      </span>
                    </th>
                    <th
                      className="setting-label"
                    >
                      <span className="setting-label-text">
                        Reasoning / 1M{' '}
                        <SettingHelp
                          align="end"
                          help={{
                            summary: 'Optional price per 1M reasoning tokens.',
                            detail:
                              'Reasoning tokens are internal “thinking” tokens some models bill separately from the visible reply (chain-of-thought / extended thinking). They are not the text you see in the chat. Leave empty if Cursor does not report or bill reasoning for that model; calculated cost then ignores this column.',
                          }}
                        />
                      </span>
                    </th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {draft.cursorTokenRates.map((rate, index) => (
                    <tr key={`${rate.model}-${index}`}>
                      <td>
                        <input
                          className="mono"
                          value={rate.model}
                          onChange={(e) => updateRate(index, { model: e.target.value })}
                          aria-label={`Model name ${index + 1}`}
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rate.inputPerMillion}
                          onChange={(e) =>
                            updateRate(index, { inputPerMillion: Number(e.target.value) })
                          }
                          aria-label={`Input rate ${index + 1}`}
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rate.cacheWritePerMillion}
                          onChange={(e) =>
                            updateRate(index, {
                              cacheWritePerMillion: Number(e.target.value),
                            })
                          }
                          aria-label={`Cache write rate ${index + 1}`}
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rate.cacheReadPerMillion}
                          onChange={(e) =>
                            updateRate(index, {
                              cacheReadPerMillion: Number(e.target.value),
                            })
                          }
                          aria-label={`Cache read rate ${index + 1}`}
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rate.outputPerMillion}
                          onChange={(e) =>
                            updateRate(index, { outputPerMillion: Number(e.target.value) })
                          }
                          aria-label={`Output rate ${index + 1}`}
                        />
                      </td>
                      <td>
                        <input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rate.reasoningPerMillion ?? ''}
                          placeholder="—"
                          onChange={(e) =>
                            updateRate(index, {
                              reasoningPerMillion:
                                e.target.value === '' ? null : Number(e.target.value),
                            })
                          }
                          aria-label={`Reasoning rate ${index + 1}`}
                        />
                      </td>
                      <td>
                        <button
                          type="button"
                          className="btn btn-danger"
                          onClick={() =>
                            setDraft((d) =>
                              d
                                ? {
                                    ...d,
                                    cursorTokenRates: d.cursorTokenRates.filter(
                                      (_, i) => i !== index,
                                    ),
                                  }
                                : d,
                            )
                          }
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Panel>
        </section>
      )}

      {tab === 'Data' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Timesheet categories</h2>
              <p>
                Manage categories used on timesheet entries. Seeded defaults are Work and Meetings.
                Deleting a category that is in use deactivates it so history stays intact.
              </p>
            </div>
            <button
              type="button"
              className="btn"
              onClick={() => {
                setNewCategoryName('');
                setCategoryMessage(null);
                setCategoryCreateOpen(true);
              }}
            >
              Add category
            </button>
          </div>

          {categoryMessage ? <p className="form-message">{categoryMessage}</p> : null}

          <Panel className="stack">
            {timesheetCategories.isError ? (
              <ErrorState
                message={
                  timesheetCategories.error instanceof Error
                    ? timesheetCategories.error.message
                    : 'Unable to list timesheet categories'
                }
              />
            ) : timesheetCategories.isLoading ? (
              <LoadingState label="Loading categories…" />
            ) : (
              <div className="stack">
                <div className="field" style={{ maxWidth: '14rem' }}>
                  <label htmlFor="category-status-filter">Status</label>
                  <select
                    id="category-status-filter"
                    value={categoryStatusFilter}
                    onChange={(e) => setCategoryStatusFilter(e.target.value)}
                  >
                    <option value="">All statuses</option>
                    <option value="active">Active</option>
                    <option value="inactive">Inactive</option>
                  </select>
                </div>
              <div className="table-wrap">
                <table className="data">
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Sort</th>
                      <th>Status</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredCategories.map((category: TimesheetCategoryDto) => (
                        <tr key={category.id}>
                          <td>{category.name}</td>
                          <td>{category.sortOrder}</td>
                          <td>
                              <StatusBadge
                                label={category.isActive ? 'Active' : 'Inactive'}
                                tone={category.isActive ? 'success' : 'neutral'}
                              />
                          </td>
                          <td>
                            <div className="row-actions">
                                  <button
                                    type="button"
                                    className="btn btn-compact btn-secondary"
                                    onClick={() => {
                                      setCategoryCreateOpen(false);
                                      setEditingCategoryId(category.id);
                                      setCategoryDraft({
                                        name: category.name,
                                        sortOrder: category.sortOrder,
                                        isActive: category.isActive,
                                      });
                                      setCategoryMessage(null);
                                    }}
                                  >
                                    Edit
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-compact btn-danger"
                                    disabled={deleteCategory.isPending}
                                    onClick={() => {
                                      const ok = window.confirm(
                                        category.isActive
                                          ? `Delete category "${category.name}"? If it is used on timesheet entries it will be deactivated instead of deleted.`
                                          : `Delete inactive category "${category.name}"?`,
                                      );
                                      if (!ok) return;
                                      setCategoryMessage(null);
                                      deleteCategory.mutate(category.id, {
                                        onSuccess: () => {
                                          setCategoryMessage('Category removed.');
                                        },
                                        onError: (err) => {
                                          setCategoryMessage(
                                            err instanceof Error
                                              ? err.message
                                              : 'Failed to remove category',
                                          );
                                        },
                                      });
                                    }}
                                  >
                                    Delete
                                  </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
              </div>
            )}
          </Panel>

          {categoryCreateOpen ? (
            <PopupForm
              title="Add category"
              contentClassName="popup-form--narrow"
              onClose={() => {
                setCategoryCreateOpen(false);
                setNewCategoryName('');
              }}
              onSubmit={(e) => {
                const event = e as FormEvent;
                event.preventDefault();
                if (!newCategoryName.trim()) {
                  return;
                }
                setCategoryMessage(null);
                createCategory.mutate(
                  { name: newCategoryName.trim() },
                  {
                    onSuccess: () => {
                      setNewCategoryName('');
                      setCategoryCreateOpen(false);
                      setCategoryMessage('Category created.');
                    },
                    onError: (err) => {
                      setCategoryMessage(
                        err instanceof Error ? err.message : 'Failed to create category',
                      );
                    },
                  },
                );
              }}
              footer={
                <>
                  <button
                    type="submit"
                    className="btn"
                    disabled={createCategory.isPending || !newCategoryName.trim()}
                  >
                    {createCategory.isPending ? 'Adding…' : 'Add category'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => {
                      setCategoryCreateOpen(false);
                      setNewCategoryName('');
                    }}
                  >
                    Cancel
                  </button>
                </>
              }
            >
              <div className="field">
                <SettingLabel
                  htmlFor="new-category-name"
                  help={{
                    summary: 'Display name for a new timesheet category.',
                    detail:
                      'Shown on timesheet entries and filters (for example Work or Meetings). Seeded defaults already include Work and Meetings; add custom names for how you classify time. Deleting a category in use deactivates it so history stays intact.',
                  }}
                >
                  Name
                </SettingLabel>
                <input
                  id="new-category-name"
                  required
                  value={newCategoryName}
                  onChange={(e) => setNewCategoryName(e.target.value)}
                  placeholder="e.g. Research"
                />
              </div>
            </PopupForm>
          ) : null}

          {editingCategoryId ? (
            <PopupForm
              title="Edit category"
              contentClassName="popup-form--narrow"
              onClose={() => setEditingCategoryId(null)}
              onSubmit={(e) => {
                const event = e as FormEvent;
                event.preventDefault();
                if (!categoryDraft.name.trim()) {
                  return;
                }
                setCategoryMessage(null);
                updateCategory.mutate(
                  {
                    id: editingCategoryId,
                    body: {
                      name: categoryDraft.name.trim(),
                      sortOrder: categoryDraft.sortOrder,
                      isActive: categoryDraft.isActive,
                    },
                  },
                  {
                    onSuccess: () => {
                      setEditingCategoryId(null);
                      setCategoryMessage('Category updated.');
                    },
                    onError: (err) => {
                      setCategoryMessage(
                        err instanceof Error ? err.message : 'Failed to update category',
                      );
                    },
                  },
                );
              }}
              footer={
                <>
                  <button
                    type="submit"
                    className="btn"
                    disabled={updateCategory.isPending || !categoryDraft.name.trim()}
                  >
                    {updateCategory.isPending ? 'Saving…' : 'Save category'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => setEditingCategoryId(null)}
                  >
                    Cancel
                  </button>
                </>
              }
            >
              <div className="stack">
                <div className="field">
                  <label htmlFor="edit-category-name">Name</label>
                  <input
                    id="edit-category-name"
                    required
                    value={categoryDraft.name}
                    onChange={(e) =>
                      setCategoryDraft((d) => ({ ...d, name: e.target.value }))
                    }
                  />
                </div>
                <div className="field">
                  <label htmlFor="edit-category-sort">Sort</label>
                  <input
                    id="edit-category-sort"
                    type="number"
                    value={categoryDraft.sortOrder}
                    onChange={(e) =>
                      setCategoryDraft((d) => ({
                        ...d,
                        sortOrder: Number(e.target.value) || 0,
                      }))
                    }
                  />
                </div>
                <label className="row">
                  <input
                    type="checkbox"
                    checked={categoryDraft.isActive}
                    onChange={(e) =>
                      setCategoryDraft((d) => ({
                        ...d,
                        isActive: e.target.checked,
                      }))
                    }
                  />
                  Active
                </label>
              </div>
            </PopupForm>
          ) : null}
        </section>
      )}

      {tab === 'API keys' && apiKeysPanel}

      {tab === 'Backup & restore' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Backup & restore</h2>
              <p>
                Choose a folder to save or load SQLite backups. Defaults to{' '}
                <span className="mono">Documents\MCP Track Tokens</span> (created automatically).
              </p>
            </div>
          </div>

          {backupInfo.isLoading && !backupInfo.data ? (
            <LoadingState label="Loading backup info…" />
          ) : null}
          {backupInfo.error ? (
            <ErrorState
              message={
                backupInfo.error instanceof Error
                  ? backupInfo.error.message
                  : 'Failed to load backup info'
              }
              error={backupInfo.error}
            />
          ) : null}

          {backupInfo.data ? (
            <Panel className="stack">
              {!backupInfo.data.supportsBackup ? (
                <div className="warning-banner" role="status">
                  Backup and restore are only available when the database provider is Sqlite (current:{' '}
                  {backupInfo.data.databaseProvider}).
                </div>
              ) : null}

              <div className="field">
                <div
                  className="label setting-label"
                >
                  Live database{' '}
                  <SettingHelp
                    help={{
                      summary: 'Live SQLite database file used by the tracking host.',
                      detail:
                        'Absolute path of the active SQLite file the server is reading and writing. Backups copy from this file; restore replaces it after a confirmation. Not used when the provider is PostgreSQL.',
                    }}
                  />
                </div>
                <strong className="mono">{backupInfo.data.databasePath || '—'}</strong>
              </div>

              <div className="field">
                <div
                  className="label setting-label"
                >
                  Backup folder{' '}
                  <SettingHelp
                    help={{
                      summary: 'Last folder selected for backups (picker default).',
                      detail:
                        'Backup now opens a folder picker that defaults to Documents\\MCP Track Tokens (or your last choice). That folder is remembered for Restore so you can pick a .db backup from the same place. Only available when the database provider is Sqlite.',
                    }}
                  />
                </div>
                <strong className="mono">
                  {backupFolderLabel || backupInfo.data.defaultFolder || 'Documents\\MCP Track Tokens'}
                </strong>
              </div>

              <div className="row">
                <button
                  type="button"
                  className="btn"
                  disabled={!backupInfo.data.supportsBackup || backupBusy}
                  onClick={() => {
                    void (async () => {
                      setBackupMessage(null);
                      setBackupBusy(true);
                      try {
                        const folder = await pickBackupFolder({
                          defaultPath: backupInfo.data.defaultFolder,
                          preferLast: false,
                        });
                        const label = folder.path ?? 'MCP Track Tokens';
                        setBackupFolderLabel(label);
                        setSelectedFolder(folder);

                        const { fileName, bytes } = await api.downloadDatabaseBackup();
                        const saved = await saveBackupToFolder(folder, fileName, bytes);
                        const files = await listLocalBackupFiles(folder);
                        setLocalBackups(files);
                        setBackupMessage(`Backup saved to ${saved}`);
                      } catch (err) {
                        if (err instanceof Error && err.message.includes('cancelled')) {
                          setBackupMessage(null);
                        } else {
                          setBackupMessage(
                            err instanceof Error ? err.message : 'Backup failed',
                          );
                        }
                      } finally {
                        setBackupBusy(false);
                      }
                    })();
                  }}
                >
                  {backupBusy ? 'Backing up…' : 'Backup now'}
                </button>
              </div>

              {backupMessage ? <p>{backupMessage}</p> : null}

              <div className="section-header" style={{ marginTop: '1rem' }}>
                <div>
                  <h3>Restore</h3>
                  <p>
                    Opens a folder selector (defaults to the last Backup now folder), lists backup
                    files there, then restores the one you choose. A safety copy of the current
                    database is saved first. Restart the tracking host afterward.
                  </p>
                </div>
              </div>

              <div className="row">
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!backupInfo.data.supportsBackup || backupBusy || restoreUpload.isPending}
                  onClick={() => {
                    void (async () => {
                      setBackupMessage(null);
                      setBackupBusy(true);
                      try {
                        const folder = await pickBackupFolder({
                          defaultPath:
                            getStoredBackupFolder() || backupInfo.data.defaultFolder,
                          preferLast: true,
                        });
                        const label = folder.path ?? getStoredBackupFolder() ?? 'MCP Track Tokens';
                        setBackupFolderLabel(label);
                        setSelectedFolder(folder);
                        const files = await listLocalBackupFiles(folder);
                        setLocalBackups(files);
                        setBackupMessage(
                          files.length
                            ? `Found ${files.length} backup file(s). Choose Restore on a row below.`
                            : 'No mcp-track-tokens-backup-*.db files in that folder.',
                        );
                      } catch (err) {
                        if (err instanceof Error && err.message.includes('cancelled')) {
                          setBackupMessage(null);
                        } else {
                          setBackupMessage(
                            err instanceof Error ? err.message : 'Could not open folder',
                          );
                        }
                      } finally {
                        setBackupBusy(false);
                      }
                    })();
                  }}
                >
                  Restore
                </button>
              </div>

              {!backupListReady ? (
                <p className="hint">Loading backups from the last folder…</p>
              ) : null}
              {backupListReady && localBackups.length === 0 ? (
                <p className="hint">
                  {selectedFolder
                    ? 'No backups found in this folder yet.'
                    : 'Use Backup now or Restore to choose a folder. After the first selection, that folder opens automatically next time.'}
                </p>
              ) : null}
              {backupListReady && localBackups.length > 0 ? (
                <div className="table-wrap">
                  <table className="data">
                    <thead>
                      <tr>
                        <th>File</th>
                        <th>Created</th>
                        <th>Size</th>
                        <th />
                      </tr>
                    </thead>
                    <tbody>
                      {localBackups.map((item) => (
                        <tr key={item.fullPath ?? item.fileName}>
                          <td className="mono">{item.fileName}</td>
                          <td>{formatDateTime(item.createdAtUtc)}</td>
                          <td>{formatBytes(item.sizeBytes)}</td>
                          <td>
                            <div className="row">
                              <button
                                type="button"
                                className="btn btn-secondary"
                                disabled={restoreUpload.isPending || backupBusy}
                                onClick={() => {
                                  void (async () => {
                                    if (
                                      !window.confirm(
                                        `Restore from “${item.fileName}”? A safety copy of the current database will be saved first.`,
                                      )
                                    ) {
                                      return;
                                    }
                                    setBackupMessage(null);
                                    try {
                                      const file = await readLocalBackupFile(item);
                                      restoreUpload.mutate(file, {
                                        onSuccess: (result) => setBackupMessage(result.message),
                                        onError: (err) =>
                                          setBackupMessage(
                                            err instanceof Error ? err.message : 'Restore failed',
                                          ),
                                      });
                                    } catch (err) {
                                      setBackupMessage(
                                        err instanceof Error ? err.message : 'Restore failed',
                                      );
                                    }
                                  })();
                                }}
                              >
                                Restore
                              </button>
                              <button
                                type="button"
                                className="btn btn-danger"
                                disabled={restoreUpload.isPending || backupBusy || !selectedFolder}
                                onClick={() => {
                                  void (async () => {
                                    if (
                                      !selectedFolder ||
                                      !window.confirm(`Delete backup “${item.fileName}”?`)
                                    ) {
                                      return;
                                    }
                                    setBackupMessage(null);
                                    setBackupBusy(true);
                                    try {
                                      await deleteLocalBackupFile(selectedFolder, item);
                                      const files = await listLocalBackupFiles(selectedFolder);
                                      setLocalBackups(files);
                                      setBackupMessage(`Deleted ${item.fileName}`);
                                    } catch (err) {
                                      setBackupMessage(
                                        err instanceof Error ? err.message : 'Delete failed',
                                      );
                                    } finally {
                                      setBackupBusy(false);
                                    }
                                  })();
                                }}
                              >
                                Delete
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : null}
            </Panel>
          ) : null}
        </section>
      )}

      {tab === 'Integrations' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Database & integrations</h2>
              <p>Local status for storage and editor integrations.</p>
            </div>
          </div>
          <Panel className="stack">
            <div className="field-row">
              <div>
                <div
                  className="label setting-label"
                >
                  Database{' '}
                  <SettingHelp
                    help={{
                      summary: 'Filesystem path of the tracking database on the server.',
                      detail:
                        'Where the tracking host stores data. For Sqlite this is a .db file path; for PostgreSQL it may reflect the configured connection target. Useful when diagnosing disk location or Docker volume mounts.',
                    }}
                  />
                </div>
                <strong className="mono">
                  {status.data?.databasePath ?? settings.data?.databasePath}
                </strong>
              </div>
              <div>
                <div
                  className="label setting-label"
                >
                  Provider{' '}
                  <SettingHelp
                    help={{
                      summary: 'Database engine in use (Sqlite or PostgreSQL).',
                      detail:
                        'Sqlite is typical for local/tray installs and enables file backup/restore. PostgreSQL is for shared or hosted deployments; backup/restore UI applies only to Sqlite.',
                    }}
                  />
                </div>
                <strong>
                  {status.data?.databaseProvider ?? settings.data?.databaseProvider}
                </strong>
              </div>
              <div>
                <div
                  className="label setting-label"
                >
                  DB health{' '}
                  <SettingHelp
                    help={{
                      summary: 'Whether the server can open and query the database.',
                      detail:
                        'OK means the API opened a connection and ran a simple health check. Check required usually means a path, permissions, or connection-string problem—inspect server logs before changing other settings.',
                    }}
                  />
                </div>
                <StatusBadge
                  label={status.data?.isHealthy ? 'OK' : 'Check required'}
                  tone={status.data?.isHealthy ? 'success' : 'warning'}
                />
              </div>
            </div>

            <div className="field-row">
              <div>
                <div
                  className="label setting-label"
                >
                  Cursor hooks{' '}
                  <SettingHelp
                    help={{
                      summary: 'Detected via hooks on disk, or inferred from recent Cursor ingest.',
                      detail:
                        'Configured means the hooks directory was found on the server host. Active (inferred) means recent Cursor ingest arrived even though the API cannot see your user ~/.cursor folder (common in Docker). Unknown means neither disk config nor recent ingest was found.',
                    }}
                  />
                </div>
                <StatusBadge
                  label={
                    integrations.data?.cursorHooksOnDisk
                      ? 'Configured'
                      : integrations.data?.cursorHooksInferredFromActivity
                        ? 'Active (inferred)'
                        : integrations.data?.cursorHooksConfigured
                          ? 'Configured'
                          : 'Unknown / not detected'
                  }
                  tone={integrations.data?.cursorHooksConfigured ? 'success' : 'warning'}
                />
              </div>
              <div>
                <div
                  className="label setting-label"
                >
                  MCP{' '}
                  <SettingHelp
                    help={{
                      summary: 'Whether MCP tooling for this server appears configured.',
                      detail:
                        'Checks whether Model Context Protocol client config for MCP Track Tokens looks present from the server. If you use MCP only on another machine, this status may stay Unknown even when tools work locally.',
                    }}
                  />
                </div>
                <StatusBadge
                  label={
                    integrations.data?.mcpConfigured ? 'Configured' : 'Unknown / not detected'
                  }
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

            <div className="stack" style={{ marginTop: '1rem' }}>
              <div className="row">
                <button
                  type="button"
                  className="btn"
                  disabled={checkCursorHooks.isPending}
                  onClick={() => checkCursorHooks.mutate()}
                >
                  {checkCursorHooks.isPending
                    ? 'Running check…'
                    : 'Run Cursor hooks compatibility check'}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={replayQueue.isPending}
                  onClick={() => replayQueue.mutate()}
                >
                  {replayQueue.isPending ? 'Replaying…' : 'Replay offline queue'}
                </button>
              </div>
              <p className="hint">
                Compatibility check probes ingest (writes a Heartbeat when successful). Replay
                flushes queued hook events from disk without waiting for the next Cursor prompt.
              </p>
              {replayQueue.isError ? (
                <ErrorState
                  message={
                    replayQueue.error instanceof Error
                      ? replayQueue.error.message
                      : 'Offline queue replay failed'
                  }
                />
              ) : null}
              {replayQueue.data ? (
                <p className="hint">
                  Replayed {replayQueue.data.flushed} of {replayQueue.data.attempted};{' '}
                  {replayQueue.data.remaining} remaining
                  {replayQueue.data.failed > 0 ? `, ${replayQueue.data.failed} failed` : ''}.
                </p>
              ) : null}
              {checkCursorHooks.isError ? (
                <ErrorState
                  message={
                    checkCursorHooks.error instanceof Error
                      ? checkCursorHooks.error.message
                      : 'Hooks compatibility check failed'
                  }
                />
              ) : null}
              {checkCursorHooks.data ? (
                <div className="stack">
                  <div className="row">
                    <StatusBadge
                      label={checkCursorHooks.data.status}
                      tone={
                        checkCursorHooks.data.status === 'compatible'
                          ? 'success'
                          : checkCursorHooks.data.status === 'degraded'
                            ? 'warning'
                            : 'danger'
                      }
                    />
                    <span>{checkCursorHooks.data.summary}</span>
                  </div>
                  {checkCursorHooks.data.cursorVersion ? (
                    <p className="hint">
                      Cursor {checkCursorHooks.data.cursorVersion}
                      {checkCursorHooks.data.cursorVersionSource
                        ? ` (${checkCursorHooks.data.cursorVersionSource})`
                        : ''}
                    </p>
                  ) : null}
                  <ul>
                    {checkCursorHooks.data.checks.map((check) => (
                      <li key={check.id}>
                        <StatusBadge
                          label={check.status}
                          tone={
                            check.status === 'pass'
                              ? 'success'
                              : check.status === 'warn'
                                ? 'warning'
                                : 'danger'
                          }
                        />{' '}
                        {check.message}
                      </li>
                    ))}
                  </ul>
                  {checkCursorHooks.data.recommendations.length > 0 ? (
                    <>
                      <h3>Recommendations</h3>
                      <ul>
                        {checkCursorHooks.data.recommendations.map((rec) => (
                          <li key={rec}>{rec}</li>
                        ))}
                      </ul>
                    </>
                  ) : null}
                </div>
              ) : null}
            </div>
          </Panel>
        </section>
      )}
    </Page>
  );
}
