import type {
  CreateProjectRequest,
  HeartbeatRequest,
  IngestEvent,
  IngestEventResult,
  ProjectDetail,
  SessionEndRequest,
  SessionStartRequest,
  TrackingStatus,
} from './types';
import { OfflineQueue } from './offlineQueue';

export interface ApiClientOptions {
  getServerUrl: () => string;
  getApiKey: () => Promise<string | undefined>;
  timeoutMs?: number;
  maxRetries?: number;
  fetchImpl?: typeof fetch;
  queue?: OfflineQueue;
  onServerReachable?: (ok: boolean) => void;
}

export class ApiClient {
  private readonly timeoutMs: number;
  private readonly maxRetries: number;
  private readonly fetchImpl: typeof fetch;
  private readonly queue?: OfflineQueue;
  private readonly onServerReachable?: (ok: boolean) => void;

  constructor(private readonly options: ApiClientOptions) {
    this.timeoutMs = options.timeoutMs ?? 5000;
    this.maxRetries = options.maxRetries ?? 2;
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.queue = options.queue;
    this.onServerReachable = options.onServerReachable;
  }

  async postEvent(event: IngestEvent): Promise<IngestEventResult | null> {
    try {
      const result = await this.requestJson<IngestEventResult>('POST', '/api/v1/events', event);
      this.onServerReachable?.(true);
      await this.flushQueue();
      return result;
    } catch (err) {
      this.onServerReachable?.(false);
      this.queue?.enqueue(event);
      throw err;
    }
  }

  async postEventSafe(event: IngestEvent): Promise<IngestEventResult | null> {
    try {
      return await this.postEvent(event);
    } catch {
      return null;
    }
  }

  async flushQueue(): Promise<number> {
    if (!this.queue) {
      return 0;
    }
    const pending = this.queue.readAll();
    if (pending.length === 0) {
      return 0;
    }

    const remaining: IngestEvent[] = [];
    let flushed = 0;
    for (const event of pending) {
      try {
        await this.requestJson<IngestEventResult>('POST', '/api/v1/events', event);
        flushed += 1;
      } catch {
        remaining.push(event);
      }
    }
    this.queue.replaceAll(remaining);
    if (flushed > 0) {
      this.onServerReachable?.(true);
    }
    return flushed;
  }

  async startSession(body: SessionStartRequest): Promise<{ id: string } | null> {
    return this.requestJson('POST', '/api/v1/sessions/start', body);
  }

  async endSession(body: SessionEndRequest): Promise<unknown> {
    return this.requestJson('POST', '/api/v1/sessions/end', body);
  }

  async heartbeat(body: HeartbeatRequest): Promise<unknown> {
    return this.requestJson('POST', '/api/v1/sessions/heartbeat', body);
  }

  async registerProject(body: CreateProjectRequest): Promise<ProjectDetail> {
    return this.requestJson('POST', '/api/v1/projects', body);
  }

  async getStatus(): Promise<TrackingStatus> {
    return this.requestJson('GET', '/api/v1/reports/summary');
  }

  async getHealth(): Promise<{ status?: string } | unknown> {
    return this.requestJson('GET', '/health');
  }

  async getUnallocated(): Promise<unknown> {
    return this.requestJson('GET', '/api/v1/unallocated');
  }

  async testConnection(): Promise<{ ok: boolean; message: string }> {
    try {
      await this.getHealth();
      this.onServerReachable?.(true);
      return { ok: true, message: 'Server is reachable.' };
    } catch (err) {
      this.onServerReachable?.(false);
      return {
        ok: false,
        message: err instanceof Error ? err.message : 'Connection failed',
      };
    }
  }

  private async requestJson<T>(
    method: string,
    apiPath: string,
    body?: unknown,
  ): Promise<T> {
    const base = this.options.getServerUrl().replace(/\/$/, '');
    const url = `${base}${apiPath}`;
    let lastError: unknown;

    for (let attempt = 0; attempt <= this.maxRetries; attempt++) {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), this.timeoutMs);
      try {
        const headers: Record<string, string> = {
          Accept: 'application/json',
        };
        if (body !== undefined) {
          headers['Content-Type'] = 'application/json';
        }
        const apiKey = await this.options.getApiKey();
        if (apiKey) {
          headers.Authorization = `Bearer ${apiKey}`;
        }

        const response = await this.fetchImpl(url, {
          method,
          headers,
          body: body === undefined ? undefined : JSON.stringify(body),
          signal: controller.signal,
        });

        if (!response.ok) {
          const text = await response.text().catch(() => '');
          throw new Error(`HTTP ${response.status}: ${text || response.statusText}`);
        }

        if (response.status === 204) {
          return undefined as T;
        }

        const text = await response.text();
        if (!text) {
          return undefined as T;
        }
        return JSON.parse(text) as T;
      } catch (err) {
        lastError = err;
        if (attempt < this.maxRetries) {
          await delay(150 * (attempt + 1));
          continue;
        }
      } finally {
        clearTimeout(timer);
      }
    }

    throw lastError instanceof Error ? lastError : new Error(String(lastError));
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
