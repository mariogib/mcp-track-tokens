import { useEffect, useState, type ReactNode } from 'react';
import {
  API_KEY_STORAGE,
  getStoredApiKey,
  setStoredApiKey,
} from '../api/client';
import {
  useApiKeysQuery,
  useCreateApiKeyMutation,
  useCreateTimesheetCategoryMutation,
  useDatabaseBackupInfoQuery,
  useDeleteTimesheetCategoryMutation,
  useIntegrationsQuery,
  useRestoreDatabaseUploadMutation,
  useRevokeApiKeyMutation,
  useSettingsQuery,
  useStatusQuery,
  useTimesheetCategoriesQuery,
  useUpdateSettingsMutation,
  useUpdateTimesheetCategoryMutation,
} from '../api/hooks';
import { api } from '../api/client';
import type {
  CursorModelTokenRateDto,
  SettingsDto,
  TimesheetCategoryDto,
  UpdateSettingsRequest,
} from '../api/types';
import { ErrorState, LoadingState } from '../components/States';
import { StatusBadge } from '../components/StatusBadge';
import { Page } from '../layout/AppLayout';
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

function SettingHelp({ text }: { text: string }) {
  return (
    <span
      className="setting-help"
      data-tooltip={text}
      title={text}
      tabIndex={0}
      role="img"
      aria-label={text}
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
  help: string;
  children: ReactNode;
}) {
  return (
    <label htmlFor={htmlFor} className="setting-label" title={help}>
      <span>{children}</span>
      <SettingHelp text={help} />
    </label>
  );
}

function SettingCheck({
  help,
  checked,
  onChange,
  children,
}: {
  help: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  children: ReactNode;
}) {
  return (
    <label className="row setting-label--row" title={help}>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span>{children}</span>
      <SettingHelp text={help} />
    </label>
  );
}

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

const SETTINGS_TABS = [
  'Connection',
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

type SettingsTab = (typeof SETTINGS_TABS)[number];

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
    autoCreateProjects: settings.autoCreateProjects,
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
  const settings = useSettingsQuery();
  const status = useStatusQuery();
  const apiKeys = useApiKeysQuery();
  const timesheetCategories = useTimesheetCategoriesQuery();
  const integrations = useIntegrationsQuery();
  const updateSettings = useUpdateSettingsMutation();
  const createKey = useCreateApiKeyMutation();
  const revokeKey = useRevokeApiKeyMutation();
  const createCategory = useCreateTimesheetCategoryMutation();
  const updateCategory = useUpdateTimesheetCategoryMutation();
  const deleteCategory = useDeleteTimesheetCategoryMutation();
  const restoreUpload = useRestoreDatabaseUploadMutation();

  const [tab, setTab] = useState<SettingsTab>('Connection');
  const [draft, setDraft] = useState<SettingsDraft | null>(null);
  const [localKey, setLocalKey] = useState(() => getStoredApiKey() ?? '');
  const [newKeyName, setNewKeyName] = useState('Dashboard');
  const [newCategoryName, setNewCategoryName] = useState('');
  const [editingCategoryId, setEditingCategoryId] = useState<string | null>(null);
  const [categoryDraft, setCategoryDraft] = useState({ name: '', sortOrder: 0, isActive: true });
  const [categoryMessage, setCategoryMessage] = useState<string | null>(null);
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

  const backupInfo = useDatabaseBackupInfoQuery(undefined, true);

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
    updateSettings.mutate(payload, {
      onSuccess: () => setMessage('Settings saved.'),
      onError: (err) =>
        setMessage(err instanceof Error ? err.message : 'Failed to save settings'),
    });
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
            Paste a key first if the rest of this page cannot load.
          </p>
        </div>
      </div>

      <div className="panel stack">
        <div className="field-row">
          <div className="field">
            <SettingLabel
              htmlFor="local-api-key"
              help="API key stored in this browser’s localStorage and sent as the Bearer token for dashboard requests. It is not the same as creating a server key until you paste one."
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
        {connectionPanel}
        <LoadingState label="Loading settings…" />
      </Page>
    );
  }

  if (settings.error && !draft) {
    return (
      <Page>
        {connectionPanel}
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
        {connectionPanel}
        <LoadingState label="Loading settings…" />
      </Page>
    );
  }

  const contentWarning = draft.storePromptContent || draft.storeResponseContent;

  return (
    <Page>
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

      {message && tab !== 'Connection' ? (
        <p className="muted" style={{ marginTop: '0.75rem' }}>
          {message}
        </p>
      ) : null}

      {tab === 'Connection' && connectionPanel}

      {tab === 'Tracking' && (
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
              saveDraft();
            }}
          >
            <div className="field-row">
              <div className="field">
                <SettingLabel
                  htmlFor="inactivity"
                  help="Gap used when calculating active project time windows. Prompts and agent events within this many minutes stay in the same window; a longer gap starts a new one. Does not close editor sessions."
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
                  help="If an open editor session for a workspace has had no prompt for longer than this, the next prompt ends that session at the last prompt time and opens a new one."
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
                  help="Default ISO currency used for projects and cost displays when a project does not override it."
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
                  help="Fixed monthly Cursor subscription amount to allocate across projects. Separate from usage-based (on-demand) spend."
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
                  help="Currency for the subscription fee amount (usually the same as your Cursor billing currency)."
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
                  help="How the subscription fee is split across projects (for example by active project time or prompt count). NotAllocated skips subscription sharing."
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
                  help="Optional automatic purge horizon in days. Leave empty for unlimited retention (no scheduled deletion)."
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
                help="Directory where CSV/JSON export files are written on the server. Must be an approved export path."
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
              help="When an event’s repository is unknown, automatically create a project instead of leaving the event unallocated."
              checked={draft.autoCreateProjects}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, autoCreateProjects: checked } : d))
              }
            >
              Auto-create projects for unknown repositories
            </SettingCheck>

            <SettingCheck
              help="Store a salted hash of prompt text for duplicate detection without keeping the raw prompt. Recommended when content storage is off."
              checked={draft.enablePromptHashing}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, enablePromptHashing: checked } : d))
              }
            >
              Enable salted prompt hashing
            </SettingCheck>

            <SettingCheck
              help="When enabled (and encryption is configured), persist full prompt text encrypted at rest. Off by default for privacy. Hooks must also send content."
              checked={draft.storePromptContent}
              onChange={(checked) =>
                setDraft((d) => (d ? { ...d, storePromptContent: checked } : d))
              }
            >
              Store prompt content (encrypted at rest)
            </SettingCheck>

            <SettingCheck
              help="When enabled (and encryption is configured), persist agent response text encrypted at rest. Off by default for privacy."
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
                title="Save tracking preferences to the server"
              >
                Save settings
              </button>
            </div>
          </form>
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
                matches.
              </p>
            </div>
          </div>

          <div className="panel stack">
            <SettingCheck
              help="When imported Cursor usage shows $0 (Included/Free), estimate spend from the rate card using token counts instead of treating cost as zero."
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
                title="Save the rate card and estimate-cost flag to the server"
                onClick={() => saveDraft()}
              >
                Save rates
              </button>
            </div>

            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th className="setting-label" title="Model name as it appears in Cursor usage exports. Use * as the fallback when no model matches.">
                      Model <SettingHelp text="Model name as it appears in Cursor usage exports. Use * as the fallback when no model matches." />
                    </th>
                    <th className="setting-label" title="Price per 1,000,000 input tokens in your currency units.">
                      Input / 1M <SettingHelp text="Price per 1,000,000 input tokens in your currency units." />
                    </th>
                    <th className="setting-label" title="Price per 1,000,000 output tokens in your currency units.">
                      Output / 1M <SettingHelp text="Price per 1,000,000 output tokens in your currency units." />
                    </th>
                    <th className="setting-label" title="Price per 1,000,000 cache-read tokens.">
                      Cache read / 1M <SettingHelp text="Price per 1,000,000 cache-read tokens." />
                    </th>
                    <th className="setting-label" title="Price per 1,000,000 cache-write tokens.">
                      Cache write / 1M <SettingHelp text="Price per 1,000,000 cache-write tokens." />
                    </th>
                    <th className="setting-label" title="Optional price per 1,000,000 reasoning tokens when the export includes them.">
                      Reasoning / 1M <SettingHelp text="Optional price per 1,000,000 reasoning tokens when the export includes them." />
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
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
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
          </div>

          <div className="panel stack">
            <div className="field-row">
              <div className="field">
                <SettingLabel
                  htmlFor="new-category-name"
                  help="Display name for a new timesheet category (for example Work or Meetings)."
                >
                  New category
                </SettingLabel>
                <input
                  id="new-category-name"
                  value={newCategoryName}
                  onChange={(e) => setNewCategoryName(e.target.value)}
                  placeholder="e.g. Research"
                />
              </div>
            </div>
            <div className="row">
              <button
                type="button"
                className="btn"
                disabled={createCategory.isPending || !newCategoryName.trim()}
                onClick={() => {
                  setCategoryMessage(null);
                  createCategory.mutate(
                    { name: newCategoryName.trim() },
                    {
                      onSuccess: () => {
                        setNewCategoryName('');
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
              >
                {createCategory.isPending ? 'Adding…' : 'Add category'}
              </button>
              {categoryMessage ? <span className="form-message">{categoryMessage}</span> : null}
            </div>

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
                    {(timesheetCategories.data ?? []).map((category: TimesheetCategoryDto) => {
                      const editing = editingCategoryId === category.id;
                      return (
                        <tr key={category.id}>
                          <td>
                            {editing ? (
                              <input
                                value={categoryDraft.name}
                                onChange={(e) =>
                                  setCategoryDraft((d) => ({ ...d, name: e.target.value }))
                                }
                              />
                            ) : (
                              category.name
                            )}
                          </td>
                          <td>
                            {editing ? (
                              <input
                                type="number"
                                value={categoryDraft.sortOrder}
                                onChange={(e) =>
                                  setCategoryDraft((d) => ({
                                    ...d,
                                    sortOrder: Number(e.target.value) || 0,
                                  }))
                                }
                                style={{ width: '5rem' }}
                              />
                            ) : (
                              category.sortOrder
                            )}
                          </td>
                          <td>
                            {editing ? (
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
                            ) : (
                              <StatusBadge
                                label={category.isActive ? 'Active' : 'Inactive'}
                                tone={category.isActive ? 'success' : 'neutral'}
                              />
                            )}
                          </td>
                          <td>
                            <div className="row-actions">
                              {editing ? (
                                <>
                                  <button
                                    type="button"
                                    className="btn btn-compact"
                                    disabled={
                                      updateCategory.isPending || !categoryDraft.name.trim()
                                    }
                                    onClick={() => {
                                      setCategoryMessage(null);
                                      updateCategory.mutate(
                                        {
                                          id: category.id,
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
                                              err instanceof Error
                                                ? err.message
                                                : 'Failed to update category',
                                            );
                                          },
                                        },
                                      );
                                    }}
                                  >
                                    Save
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-compact btn-secondary"
                                    onClick={() => setEditingCategoryId(null)}
                                  >
                                    Cancel
                                  </button>
                                </>
                              ) : (
                                <>
                                  <button
                                    type="button"
                                    className="btn btn-compact btn-secondary"
                                    onClick={() => {
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
                                          ? `Remove category "${category.name}"? If it is used on timesheet entries it will be deactivated instead of deleted.`
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
                                </>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </section>
      )}

      {tab === 'API keys' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>API key management</h2>
              <p>
                Create and revoke server API keys. The browser still uses the localStorage key above.
              </p>
            </div>
          </div>

          <div className="panel stack">
            <div className="field-row">
              <div className="field">
                <SettingLabel
                  htmlFor="new-key-name"
                  help="Friendly name for a new server API key. The plaintext key is shown once after creation; store it securely."
                >
                  Create server API key
                </SettingLabel>
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
                title="Create a new server API key with the name above. The plaintext secret is shown only once."
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
                  apiKeys.error instanceof Error
                    ? apiKeys.error.message
                    : 'Unable to list API keys'
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
      )}

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
            <div className="panel stack">
              {!backupInfo.data.supportsBackup ? (
                <div className="warning-banner" role="status">
                  Backup and restore are only available when the database provider is Sqlite (current:{' '}
                  {backupInfo.data.databaseProvider}).
                </div>
              ) : null}

              <div className="field">
                <div
                  className="label setting-label"
                  title="Live SQLite database file used by the tracking host."
                >
                  Live database{' '}
                  <SettingHelp text="Live SQLite database file used by the tracking host." />
                </div>
                <strong className="mono">{backupInfo.data.databasePath || '—'}</strong>
              </div>

              <div className="field">
                <div
                  className="label setting-label"
                  title="Last folder selected for backups. Backup now opens a folder picker defaulting here."
                >
                  Backup folder{' '}
                  <SettingHelp text="Last folder selected for backups. Backup now opens a folder picker defaulting to Documents\MCP Track Tokens, then remembers your choice for Restore." />
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
            </div>
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
          <div className="panel stack">
            <div className="field-row">
              <div>
                <div className="label setting-label" title="Filesystem path of the tracking database on the server.">
                  Database <SettingHelp text="Filesystem path of the tracking database on the server." />
                </div>
                <strong className="mono">
                  {status.data?.databasePath ?? settings.data?.databasePath}
                </strong>
              </div>
              <div>
                <div className="label setting-label" title="Database engine in use (Sqlite or PostgreSQL).">
                  Provider <SettingHelp text="Database engine in use (Sqlite or PostgreSQL)." />
                </div>
                <strong>
                  {status.data?.databaseProvider ?? settings.data?.databaseProvider}
                </strong>
              </div>
              <div>
                <div className="label setting-label" title="Whether the server can open and query the database successfully.">
                  DB health <SettingHelp text="Whether the server can open and query the database successfully." />
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
                  title="Detected via the hooks directory on the server host, or inferred from recent Cursor ingest when the API runs in Docker and cannot see your user ~/.cursor folder."
                >
                  Cursor hooks{' '}
                  <SettingHelp text="Detected via the hooks directory on the server host, or inferred from recent Cursor ingest when the API runs in Docker and cannot see your user ~/.cursor folder." />
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
                <div className="label setting-label" title="Whether the VS Code extension for MCP Track Tokens was detected from the server’s perspective.">
                  VS Code extension <SettingHelp text="Whether the VS Code extension for MCP Track Tokens was detected from the server’s perspective." />
                </div>
                <StatusBadge
                  label={
                    integrations.data?.vscodeExtensionDetected
                      ? 'Detected'
                      : 'Unknown / not detected'
                  }
                  tone={integrations.data?.vscodeExtensionDetected ? 'success' : 'warning'}
                />
              </div>
              <div>
                <div className="label setting-label" title="Whether MCP (Model Context Protocol) tooling for this server appears configured.">
                  MCP <SettingHelp text="Whether MCP (Model Context Protocol) tooling for this server appears configured." />
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
          </div>
        </section>
      )}
    </Page>
  );
}
