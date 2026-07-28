import type {
  ActiveSessionDto,
  AllocationRequestDto,
  ApiKeyCreateResultDto,
  ApiKeyDto,
  CreateApiKeyRequestDto,
  ExportRequestDto,
  HealthDto,
  ImportPreviewDto,
  ImportResultDto,
  ImportedUsageReport,
  IntegrationStatusDto,
  DatabaseBackupInfoDto,
  DatabaseBackupResultDto,
  DatabaseRestoreResultDto,
  ClientCostReport,
  ClientTokenCostEstimate,
  EditorComparisonReport,
  ModelCostReport,
  MonthlySummaryReport,
  ProjectActivityReport,
  ProjectCostReport,
  ReportClientDto,
  ProjectTokenCostEstimate,
  ProjectDetailDto,
  ProjectDto,
  PromptEventDto,
  PromptBrowseQuery,
  PromptFacetsDto,
  PagedResult,
  ReconciliationRequestDto,
  ReconciliationResultDto,
  CreateProjectSessionRequest,
  CreateTimesheetCategoryRequest,
  CreateTimesheetEntryRequest,
  EndTimesheetRequest,
  SessionBrowseQuery,
  SessionDto,
  SettingsDto,
  StartTimesheetRequest,
  TimesheetCategoryDto,
  TimesheetClientReport,
  TimesheetEntryDto,
  TimesheetBrowseQuery,
  TimesheetMonthAvailabilityDto,
  TimesheetOverallReport,
  TimesheetProjectReport,
  TrackingStatusDto,
  UpdateTimesheetCategoryRequest,
  AssignActivityRequestDto,
  AssignActivityResultDto,
  CreateProjectRequest,
  DeleteActivityRequestDto,
  DeleteActivityResultDto,
  UnallocatedBundle,
  UnallocatedItemDto,
  UnallocatedUsageReport,
  DeleteUnallocatedUsageResultDto,
  CursorDocsPricingFetchResultDto,
  CursorHooksCompatibilityReportDto,
  OfflineQueueReplayResultDto,
  UpdateProjectRequest,
  UpdateSessionRequest,
  UpdateSettingsRequest,
  UpdateTimesheetEntryRequest,
  UsageAttributionRow,
  UsageSummaryDto,
} from './types';

export const API_KEY_STORAGE = 'mcp-track-tokens-api-key';

export function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_URL?.trim();
  return (raw && raw.length > 0 ? raw : 'http://127.0.0.1:5187').replace(/\/$/, '');
}

export function getStoredApiKey(): string | null {
  try {
    return localStorage.getItem(API_KEY_STORAGE);
  } catch {
    return null;
  }
}

export function setStoredApiKey(key: string | null): void {
  try {
    if (!key) {
      localStorage.removeItem(API_KEY_STORAGE);
    } else {
      localStorage.setItem(API_KEY_STORAGE, key);
    }
  } catch {
    /* ignore storage failures in private mode */
  }
}

export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, message: string, body?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

type RequestOptions = {
  method?: string;
  body?: unknown;
  formData?: FormData;
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
  auth?: boolean;
};

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const base = getApiBaseUrl();
  const url = new URL(path.startsWith('http') ? path : `${base}${path.startsWith('/') ? path : `/${path}`}`);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === '') continue;
      url.searchParams.set(key, String(value));
    }
  }
  return url.toString();
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };

  if (options.auth !== false) {
    const key = getStoredApiKey();
    if (key) {
      headers.Authorization = `Bearer ${key}`;
    }
  }

  let body: BodyInit | undefined;
  if (options.formData) {
    body = options.formData;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }

  const response = await fetch(buildUrl(path, options.query), {
    method: options.method ?? (options.body || options.formData ? 'POST' : 'GET'),
    headers,
    body,
    signal: options.signal,
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('application/json')
    ? await response.json().catch(() => null)
    : await response.text().catch(() => null);

  if (!response.ok) {
    const messageFromBody =
      (payload &&
        typeof payload === 'object' &&
        'error' in payload &&
        String((payload as { error: unknown }).error)) ||
      (payload &&
        typeof payload === 'object' &&
        'title' in payload &&
        String((payload as { title: unknown }).title)) ||
      (payload &&
        typeof payload === 'object' &&
        'detail' in payload &&
        String((payload as { detail: unknown }).detail)) ||
      (typeof payload === 'string' && payload) ||
      null;

    const validationErrors =
      payload &&
      typeof payload === 'object' &&
      'errors' in payload &&
      payload.errors &&
      typeof payload.errors === 'object'
        ? Object.entries(payload.errors as Record<string, unknown>)
            .flatMap(([key, value]) =>
              Array.isArray(value) ? value.map((item) => `${key}: ${String(item)}`) : [`${key}: ${String(value)}`],
            )
            .join(' ')
        : null;

    let message =
      validationErrors || messageFromBody || `Request failed (${response.status})`;
    if (response.status === 401) {
      message = getStoredApiKey()
        ? 'Unauthorized (401). The stored API key was rejected — update it under Settings.'
        : 'Unauthorized (401). Set a Bearer API key under Settings to call the tracking API.';
    }
    if (response.status === 413) {
      message =
        messageFromBody ||
        'Upload too large (413). The backup file exceeds the server restore size limit.';
    }

    throw new ApiError(response.status, message, payload);
  }

  if (typeof payload === 'string' && payload.trimStart().startsWith('<!')) {
    throw new ApiError(
      response.status,
      `Expected JSON from ${path} but received HTML (route may be missing).`,
      payload,
    );
  }

  return payload as T;
}

export const api = {
  health: (signal?: AbortSignal) =>
    apiRequest<HealthDto>('/health', { auth: false, signal }),

  ready: (signal?: AbortSignal) =>
    apiRequest<HealthDto>('/ready', { auth: false, signal }),

  status: (signal?: AbortSignal) =>
    apiRequest<TrackingStatusDto>('/api/v1/status', { signal }),

  reportsSummary: (year?: number, month?: number, signal?: AbortSignal) => {
    const now = new Date();
    return apiRequest<MonthlySummaryReport>('/api/v1/reports/summary', {
      query: {
        year: year ?? now.getUTCFullYear(),
        month: month ?? now.getUTCMonth() + 1,
      },
      signal,
    });
  },

  listReportClients: (signal?: AbortSignal) =>
    apiRequest<ReportClientDto[]>('/api/v1/reports/clients', { signal }),

  getClientCost: (clientName: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ClientCostReport>(
      `/api/v1/reports/clients/${encodeURIComponent(clientName)}/cost`,
      {
        query: { fromUtc, toUtc },
        signal,
      },
    ),

  getClientTokenCost: (clientName: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ClientTokenCostEstimate>(
      `/api/v1/reports/clients/${encodeURIComponent(clientName)}/token-cost`,
      {
        query: { fromUtc, toUtc },
        signal,
      },
    ),

  getModelCostReport: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ModelCostReport>('/api/v1/reports/model-cost', {
      query: { fromUtc, toUtc },
      signal,
    }),

  getEditorComparisonReport: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<EditorComparisonReport>('/api/v1/reports/editors', {
      query: { fromUtc, toUtc },
      signal,
    }),

  listProjects: (signal?: AbortSignal) =>
    apiRequest<ProjectDto[]>('/api/v1/projects', { signal }),

  createProject: (body: CreateProjectRequest, signal?: AbortSignal) =>
    apiRequest<ProjectDetailDto>('/api/v1/projects', {
      method: 'POST',
      body,
      signal,
    }),

  getProject: (id: string, signal?: AbortSignal) =>
    apiRequest<ProjectDetailDto>(`/api/v1/projects/${id}`, { signal }),

  getProjectActivity: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ProjectActivityReport>(`/api/v1/projects/${id}/activity`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectUsage: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<UsageSummaryDto>(`/api/v1/projects/${id}/usage`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectCost: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ProjectCostReport>(`/api/v1/projects/${id}/cost`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectTokenCost: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ProjectTokenCostEstimate>(`/api/v1/projects/${id}/token-cost`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectPrompts: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<PromptEventDto[]>(`/api/v1/projects/${id}/prompts`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectPromptsPaged: (
    id: string,
    query: PromptBrowseQuery,
    signal?: AbortSignal,
  ) =>
    apiRequest<PagedResult<PromptEventDto>>(`/api/v1/projects/${id}/prompts`, {
      query: {
        fromUtc: query.fromUtc,
        toUtc: query.toUtc,
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        search: query.search,
        status: query.status,
        eventType: query.eventType,
        model: query.model,
        branch: query.branch,
      },
      signal,
    }),

  getProjectPromptFacets: (
    id: string,
    fromUtc: string,
    toUtc: string,
    signal?: AbortSignal,
  ) =>
    apiRequest<PromptFacetsDto>(`/api/v1/projects/${id}/prompts/facets`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectSessions: (id: string, fromUtc?: string, toUtc?: string, signal?: AbortSignal) =>
    apiRequest<SessionDto[]>(`/api/v1/projects/${id}/sessions`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectSessionsPaged: (
    id: string,
    query: SessionBrowseQuery,
    signal?: AbortSignal,
  ) =>
    apiRequest<PagedResult<SessionDto>>(`/api/v1/projects/${id}/sessions`, {
      query: {
        fromUtc: query.fromUtc,
        toUtc: query.toUtc,
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        search: query.search,
        status: query.status,
      },
      signal,
    }),

  getSessions: (
    params?: { projectId?: string; fromUtc?: string; toUtc?: string },
    signal?: AbortSignal,
  ) =>
    apiRequest<SessionDto[]>('/api/v1/sessions', {
      query: {
        projectId: params?.projectId,
        fromUtc: params?.fromUtc,
        toUtc: params?.toUtc,
      },
      signal,
    }),

  getSessionPrompts: (id: string, signal?: AbortSignal) =>
    apiRequest<PromptEventDto[]>(`/api/v1/sessions/${id}/prompts`, { signal }),

  createProjectSession: (
    projectId: string,
    body: CreateProjectSessionRequest,
    signal?: AbortSignal,
  ) =>
    apiRequest<SessionDto>(`/api/v1/projects/${projectId}/sessions`, {
      method: 'POST',
      body,
      signal,
    }),

  updateSession: (id: string, body: UpdateSessionRequest, signal?: AbortSignal) =>
    apiRequest<SessionDto>(`/api/v1/sessions/${id}`, {
      method: 'PUT',
      body,
      signal,
    }),

  deleteSession: (id: string, signal?: AbortSignal) =>
    apiRequest<void>(`/api/v1/sessions/${id}`, {
      method: 'DELETE',
      signal,
    }),

  getProjectTimesheetEntries: (
    id: string,
    fromUtc?: string,
    toUtc?: string,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetEntryDto[]>(`/api/v1/projects/${id}/timesheet-entries`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectTimesheetEntriesPaged: (
    id: string,
    query: TimesheetBrowseQuery,
    signal?: AbortSignal,
  ) =>
    apiRequest<PagedResult<TimesheetEntryDto>>(`/api/v1/projects/${id}/timesheet-entries`, {
      query: {
        fromUtc: query.fromUtc,
        toUtc: query.toUtc,
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        search: query.search,
        openClosed: query.openClosed,
      },
      signal,
    }),

  getTimesheetEntries: (
    params?: {
      projectId?: string;
      fromUtc?: string;
      toUtc?: string;
    },
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetEntryDto[]>('/api/v1/timesheet-entries', {
      query: {
        projectId: params?.projectId,
        fromUtc: params?.fromUtc,
        toUtc: params?.toUtc,
      },
      signal,
    }),

  getTimesheetEntriesPaged: (query: TimesheetBrowseQuery, signal?: AbortSignal) =>
    apiRequest<PagedResult<TimesheetEntryDto>>('/api/v1/timesheet-entries', {
      query: {
        projectId: query.projectId,
        fromUtc: query.fromUtc,
        toUtc: query.toUtc,
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        search: query.search,
        openClosed: query.openClosed,
      },
      signal,
    }),

  createProjectTimesheetEntry: (
    projectId: string,
    body: CreateTimesheetEntryRequest,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetEntryDto>(`/api/v1/projects/${projectId}/timesheet-entries`, {
      method: 'POST',
      body,
      signal,
    }),

  startTimesheet: (body: StartTimesheetRequest, signal?: AbortSignal) =>
    apiRequest<TimesheetEntryDto>('/api/v1/timesheet/start', {
      method: 'POST',
      body,
      signal,
    }),

  endTimesheet: (body: EndTimesheetRequest, signal?: AbortSignal) =>
    apiRequest<TimesheetEntryDto>('/api/v1/timesheet/end', {
      method: 'POST',
      body,
      signal,
    }),

  updateTimesheetEntry: (id: string, body: UpdateTimesheetEntryRequest, signal?: AbortSignal) =>
    apiRequest<TimesheetEntryDto>(`/api/v1/timesheet-entries/${id}`, {
      method: 'PUT',
      body,
      signal,
    }),

  deleteTimesheetEntry: (id: string, signal?: AbortSignal) =>
    apiRequest<void>(`/api/v1/timesheet-entries/${id}`, {
      method: 'DELETE',
      signal,
    }),

  getTimesheetOverallReport: (
    fromUtc: string,
    toUtc: string,
    timeZoneOffsetMinutes?: number,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetOverallReport>('/api/v1/timesheet/reports/overall', {
      query: { fromUtc, toUtc, timeZoneOffsetMinutes },
      signal,
    }),

  getTimesheetReportMonths: (
    query?: { projectId?: string; clientName?: string },
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetMonthAvailabilityDto[]>('/api/v1/timesheet/reports/months', {
      query: {
        projectId: query?.projectId,
        clientName: query?.clientName,
      },
      signal,
    }),

  getTimesheetProjectReport: (
    projectId: string,
    fromUtc: string,
    toUtc: string,
    timeZoneOffsetMinutes?: number,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetProjectReport>(`/api/v1/timesheet/reports/projects/${projectId}`, {
      query: { fromUtc, toUtc, timeZoneOffsetMinutes },
      signal,
    }),

  getTimesheetClientReport: (
    clientName: string,
    fromUtc: string,
    toUtc: string,
    timeZoneOffsetMinutes?: number,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetClientReport>(
      `/api/v1/timesheet/reports/clients/${encodeURIComponent(clientName)}`,
      {
        query: { fromUtc, toUtc, timeZoneOffsetMinutes },
        signal,
      },
    ),

  updateProject: (id: string, body: UpdateProjectRequest, signal?: AbortSignal) =>
    apiRequest<ProjectDetailDto>(`/api/v1/projects/${id}`, {
      method: 'PUT',
      body,
      signal,
    }),

  deleteProject: (id: string, signal?: AbortSignal) =>
    apiRequest<void>(`/api/v1/projects/${id}`, {
      method: 'DELETE',
      signal,
    }),

  activeSession: (signal?: AbortSignal) =>
    apiRequest<ActiveSessionDto | null>('/api/v1/sessions/active', { signal }),

  unallocated: (fromUtc?: string, toUtc?: string, signal?: AbortSignal) =>
    apiRequest<UnallocatedBundle>('/api/v1/unallocated', {
      query: { fromUtc, toUtc },
      signal,
    }),

  unallocatedUsage: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<UnallocatedUsageReport>('/api/v1/unallocated/usage', {
      query: { fromUtc, toUtc },
      signal,
    }),

  deleteUnallocatedUsage: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<DeleteUnallocatedUsageResultDto>('/api/v1/unallocated/usage', {
      method: 'DELETE',
      query: { fromUtc, toUtc },
      signal,
    }),

  importedUsage: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<ImportedUsageReport>('/api/v1/usage/imported', {
      query: { fromUtc, toUtc },
      signal,
    }),

  unallocatedActivity: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<UnallocatedItemDto[]>('/api/v1/unallocated/activity', {
      query: { fromUtc, toUtc },
      signal,
    }),

  assignActivity: (body: AssignActivityRequestDto, signal?: AbortSignal) =>
    apiRequest<AssignActivityResultDto>('/api/v1/activity/assign', {
      method: 'POST',
      body,
      signal,
    }),

  deleteActivity: (body: DeleteActivityRequestDto, signal?: AbortSignal) =>
    apiRequest<DeleteActivityResultDto>('/api/v1/activity/delete', {
      method: 'POST',
      body,
      signal,
    }),

  previewImportUpload: (file: File, signal?: AbortSignal) => {
    const form = new FormData();
    form.append('file', file);
    return apiRequest<ImportPreviewDto>('/api/v1/imports/cursor/upload', {
      method: 'POST',
      formData: form,
      query: { preview: true },
      signal,
    });
  },

  importCursorUpload: (
    file: File,
    options: {
      dryRun?: boolean;
      force?: boolean;
      columnMappings?: Record<string, string>;
      timezone?: string;
    } = {},
    signal?: AbortSignal,
  ) => {
    const form = new FormData();
    form.append('file', file);
    if (options.columnMappings) {
      form.append('columnMappings', JSON.stringify(options.columnMappings));
    }
    if (options.timezone) {
      form.append('timezone', options.timezone);
    }
    return apiRequest<ImportResultDto>('/api/v1/imports/cursor/upload', {
      method: 'POST',
      formData: form,
      query: {
        dryRun: options.dryRun ?? false,
        force: options.force ?? false,
      },
      signal,
    });
  },

  importCursor: (
    body: {
      filePath: string;
      format?: string;
      timezone?: string;
      dryRun?: boolean;
      force?: boolean;
      columnMappings?: Record<string, string>;
    },
    signal?: AbortSignal,
  ) =>
    apiRequest<ImportResultDto>('/api/v1/imports/cursor', {
      method: 'POST',
      body,
      signal,
    }),

  runReconciliation: (body: ReconciliationRequestDto, signal?: AbortSignal) =>
    apiRequest<ReconciliationResultDto>('/api/v1/reconciliation/run', {
      method: 'POST',
      body,
      signal,
    }),

  allocateUsage: (body: AllocationRequestDto, signal?: AbortSignal) =>
    apiRequest<UsageAttributionRow[]>('/api/v1/usage/allocate', {
      method: 'POST',
      body,
      signal,
    }),

  allocateUsageToClosestPrompt: (usageRecordId: string, signal?: AbortSignal) =>
    apiRequest<UsageAttributionRow[]>(`/api/v1/usage/${usageRecordId}/allocate-to-prompt`, {
      method: 'POST',
      signal,
    }),

  getSettings: (signal?: AbortSignal) =>
    apiRequest<SettingsDto>('/api/v1/settings', { signal }),

  updateSettings: (body: UpdateSettingsRequest, signal?: AbortSignal) =>
    apiRequest<SettingsDto>('/api/v1/settings', {
      method: 'PUT',
      body,
      signal,
    }),

  fetchCursorTokenRates: (signal?: AbortSignal) =>
    apiRequest<CursorDocsPricingFetchResultDto>('/api/v1/settings/cursor-token-rates/fetch', {
      method: 'POST',
      signal,
    }),

  listApiKeys: (signal?: AbortSignal) =>
    apiRequest<ApiKeyDto[]>('/api/v1/api-keys', { signal }),

  createApiKey: (body: CreateApiKeyRequestDto, signal?: AbortSignal) =>
    apiRequest<ApiKeyCreateResultDto>('/api/v1/api-keys', {
      method: 'POST',
      body,
      signal,
    }),

  revokeApiKey: (id: string, signal?: AbortSignal) =>
    apiRequest<void>(`/api/v1/api-keys/${id}`, {
      method: 'DELETE',
      signal,
    }),

  listTimesheetCategories: (activeOnly?: boolean, signal?: AbortSignal) =>
    apiRequest<TimesheetCategoryDto[]>('/api/v1/timesheet-categories', {
      query: { activeOnly: activeOnly === undefined ? undefined : String(activeOnly) },
      signal,
    }),

  createTimesheetCategory: (body: CreateTimesheetCategoryRequest, signal?: AbortSignal) =>
    apiRequest<TimesheetCategoryDto>('/api/v1/timesheet-categories', {
      method: 'POST',
      body,
      signal,
    }),

  updateTimesheetCategory: (
    id: string,
    body: UpdateTimesheetCategoryRequest,
    signal?: AbortSignal,
  ) =>
    apiRequest<TimesheetCategoryDto>(`/api/v1/timesheet-categories/${id}`, {
      method: 'PUT',
      body,
      signal,
    }),

  deleteTimesheetCategory: (id: string, signal?: AbortSignal) =>
    apiRequest<void>(`/api/v1/timesheet-categories/${id}`, {
      method: 'DELETE',
      signal,
    }),

  exportReport: async (
    body: ExportRequestDto,
    signal?: AbortSignal,
  ): Promise<{ fileName: string; byteCount: number }> => {
    const headers: Record<string, string> = {
      Accept: 'application/json, text/csv, text/markdown, application/octet-stream',
    };
    const key = getStoredApiKey();
    if (key) {
      headers.Authorization = `Bearer ${key}`;
    }
    headers['Content-Type'] = 'application/json';

    const response = await fetch(buildUrl('/api/v1/exports'), {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
      signal,
    });

    if (!response.ok) {
      let message = `Request failed (${response.status})`;
      const contentType = response.headers.get('content-type') ?? '';
      if (contentType.includes('application/json')) {
        try {
          const payload = await response.json();
          if (payload && typeof payload === 'object' && 'error' in payload) {
            message = String((payload as { error: unknown }).error);
          } else if (payload && typeof payload === 'object' && 'title' in payload) {
            message = String((payload as { title: unknown }).title);
          }
        } catch {
          /* ignore */
        }
      }
      if (response.status === 401) {
        message = getStoredApiKey()
          ? 'Unauthorized (401). The stored API key was rejected — update it under Settings.'
          : 'Unauthorized (401). Set a Bearer API key under Settings to call the tracking API.';
      }
      throw new ApiError(response.status, message);
    }

    const disposition = response.headers.get('content-disposition') ?? '';
    const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(disposition);
    const fileName = match
      ? decodeURIComponent(match[1].replace(/"/g, ''))
      : `mcp-track-tokens-export.${String(body.format ?? 'json').toLowerCase()}`;
    const bytes = await response.arrayBuffer();
    const blob = new Blob([bytes], {
      type: response.headers.get('content-type') ?? 'application/octet-stream',
    });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
    return { fileName, byteCount: bytes.byteLength };
  },

  integrationStatus: (signal?: AbortSignal) =>
    apiRequest<IntegrationStatusDto>('/api/v1/integrations/status', { signal }),

  checkCursorHooks: (signal?: AbortSignal) =>
    apiRequest<CursorHooksCompatibilityReportDto>('/api/v1/integrations/cursor-hooks/check', {
      method: 'POST',
      signal,
    }),

  replayOfflineQueue: (signal?: AbortSignal) =>
    apiRequest<OfflineQueueReplayResultDto>('/api/v1/integrations/offline-queue/replay', {
      method: 'POST',
      signal,
    }),

  databaseBackupInfo: (destinationDirectory?: string, signal?: AbortSignal) =>
    apiRequest<DatabaseBackupInfoDto>('/api/v1/database/backup-info', {
      query: { destinationDirectory },
      signal,
    }),

  backupDatabase: (destinationDirectory?: string, signal?: AbortSignal) =>
    apiRequest<DatabaseBackupResultDto>('/api/v1/database/backup', {
      method: 'POST',
      body: { destinationDirectory: destinationDirectory || null },
      signal,
    }),

  downloadDatabaseBackup: async (
    signal?: AbortSignal,
  ): Promise<{ fileName: string; bytes: ArrayBuffer }> => {
    const headers: Record<string, string> = { Accept: 'application/x-sqlite3' };
    const key = getStoredApiKey();
    if (key) {
      headers.Authorization = `Bearer ${key}`;
    }

    const response = await fetch(buildUrl('/api/v1/database/backup-download'), {
      method: 'GET',
      headers,
      signal,
    });

    if (!response.ok) {
      let message = `Backup download failed (${response.status})`;
      try {
        const payload = await response.json();
        if (payload && typeof payload === 'object' && 'error' in payload) {
          message = String((payload as { error: unknown }).error);
        }
      } catch {
        /* ignore */
      }
      throw new ApiError(response.status, message);
    }

    const disposition = response.headers.get('content-disposition') ?? '';
    const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(disposition);
    const fileName = match
      ? decodeURIComponent(match[1].replace(/"/g, ''))
      : `mcp-track-tokens-backup-${new Date().toISOString().replace(/[:.]/g, '-')}.db`;
    const bytes = await response.arrayBuffer();
    return { fileName, bytes };
  },

  restoreDatabase: (sourceFilePath: string, signal?: AbortSignal) =>
    apiRequest<DatabaseRestoreResultDto>('/api/v1/database/restore', {
      method: 'POST',
      body: { sourceFilePath },
      signal,
    }),

  restoreDatabaseUpload: (file: File, signal?: AbortSignal) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiRequest<DatabaseRestoreResultDto>('/api/v1/database/restore-upload', {
      method: 'POST',
      formData,
      signal,
    });
  },
};
