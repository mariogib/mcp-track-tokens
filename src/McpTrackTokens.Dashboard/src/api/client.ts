import type {
  ActiveSessionDto,
  AllocationRequestDto,
  ApiKeyCreateResultDto,
  ApiKeyDto,
  CreateApiKeyRequestDto,
  ExportRequestDto,
  ExportResultDto,
  HealthDto,
  ImportPreviewDto,
  ImportResultDto,
  IntegrationStatusDto,
  MonthlySummaryReport,
  ProjectActivityReport,
  ProjectCostReport,
  ProjectDetailDto,
  ProjectDto,
  PromptEventDto,
  ReconciliationRequestDto,
  ReconciliationResultDto,
  SessionDto,
  SettingsDto,
  TrackingStatusDto,
  UnallocatedItemDto,
  UnallocatedUsageReport,
  UpdateProjectRequest,
  UpdateSettingsRequest,
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

    let message = messageFromBody || `Request failed (${response.status})`;
    if (response.status === 401) {
      message = getStoredApiKey()
        ? 'Unauthorized (401). The stored API key was rejected — update it under Settings.'
        : 'Unauthorized (401). Set a Bearer API key under Settings to call the tracking API.';
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

  listProjects: (signal?: AbortSignal) =>
    apiRequest<ProjectDto[]>('/api/v1/projects', { signal }),

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

  getProjectPrompts: (id: string, fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<PromptEventDto[]>(`/api/v1/projects/${id}/prompts`, {
      query: { fromUtc, toUtc },
      signal,
    }),

  getProjectSessions: (id: string, fromUtc?: string, toUtc?: string, signal?: AbortSignal) =>
    apiRequest<SessionDto[]>(`/api/v1/projects/${id}/sessions`, {
      query: { fromUtc, toUtc },
      signal,
    }),

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
    apiRequest<UnallocatedItemDto[] | UnallocatedUsageReport>('/api/v1/unallocated', {
      query: { fromUtc, toUtc },
      signal,
    }),

  unallocatedUsage: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<UnallocatedUsageReport>('/api/v1/unallocated/usage', {
      query: { fromUtc, toUtc },
      signal,
    }),

  unallocatedActivity: (fromUtc: string, toUtc: string, signal?: AbortSignal) =>
    apiRequest<UnallocatedItemDto[]>('/api/v1/unallocated/activity', {
      query: { fromUtc, toUtc },
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

  getSettings: (signal?: AbortSignal) =>
    apiRequest<SettingsDto>('/api/v1/settings', { signal }),

  updateSettings: (body: UpdateSettingsRequest, signal?: AbortSignal) =>
    apiRequest<SettingsDto>('/api/v1/settings', {
      method: 'PUT',
      body,
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

  exportReport: (body: ExportRequestDto, signal?: AbortSignal) =>
    apiRequest<ExportResultDto>('/api/v1/exports', {
      method: 'POST',
      body,
      signal,
    }),

  integrationStatus: (signal?: AbortSignal) =>
    apiRequest<IntegrationStatusDto>('/api/v1/integrations/status', { signal }),
};
