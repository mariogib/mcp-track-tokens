import * as fs from 'fs';
import * as path from 'path';
import type { HookConfig, TrackingEvent } from './shared';

export const DEFAULT_MAX_QUEUED_EVENTS = 10_000;

export interface SendEventOptions {
  config: HookConfig;
  fetchImpl?: typeof fetch;
  fsImpl?: Pick<
    typeof fs,
    'existsSync' | 'mkdirSync' | 'readFileSync' | 'writeFileSync' | 'appendFileSync' | 'chmodSync'
  >;
}

/**
 * POST with <=2s timeout, retry once, queue to JSONL, flush on later calls.
 * Fail-safe: never throws to the caller in runHook path (use sendEventSafe).
 */
export async function sendEvent(
  event: TrackingEvent,
  options: SendEventOptions,
): Promise<{ ok: boolean; queued: boolean }> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const fsImpl = options.fsImpl ?? fs;
  const { config } = options;

  await flushQueue({ config, fetchImpl, fsImpl });

  try {
    await postWithRetry(event, config, fetchImpl);
    return { ok: true, queued: false };
  } catch {
    enqueue(event, config.queuePath, fsImpl, config.maxQueuedEvents);
    return { ok: false, queued: true };
  }
}

export async function sendEventSafe(
  event: TrackingEvent,
  options: SendEventOptions,
): Promise<void> {
  try {
    await sendEvent(event, options);
  } catch {
    // fail-safe
  }
}

async function postWithRetry(
  event: TrackingEvent,
  config: HookConfig,
  fetchImpl: typeof fetch,
): Promise<void> {
  let lastError: unknown;
  for (let attempt = 0; attempt < 2; attempt++) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), config.timeoutMs);
    try {
      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        Accept: 'application/json',
      };
      if (config.apiKey) {
        headers.Authorization = `Bearer ${config.apiKey}`;
      }
      const response = await fetchImpl(`${config.serverUrl}/api/v1/events`, {
        method: 'POST',
        headers,
        body: JSON.stringify(event),
        signal: controller.signal,
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      return;
    } catch (err) {
      lastError = err;
    } finally {
      clearTimeout(timer);
    }
  }
  throw lastError instanceof Error ? lastError : new Error(String(lastError));
}

export async function flushQueue(options: SendEventOptions): Promise<number> {
  const fsImpl = options.fsImpl ?? fs;
  const fetchImpl = options.fetchImpl ?? fetch;
  const queuePath = options.config.queuePath;
  if (!fsImpl.existsSync(queuePath)) {
    return 0;
  }

  const raw = fsImpl.readFileSync(queuePath, 'utf8');
  const lines = raw.split(/\r?\n/).filter((l) => l.trim());
  if (lines.length === 0) {
    return 0;
  }

  const remaining: string[] = [];
  let flushed = 0;
  for (const line of lines) {
    try {
      const event = JSON.parse(line) as TrackingEvent;
      await postWithRetry(event, options.config, fetchImpl);
      flushed += 1;
    } catch {
      remaining.push(line);
    }
  }

  ensureDir(path.dirname(queuePath), fsImpl);
  fsImpl.writeFileSync(queuePath, remaining.length ? `${remaining.join('\n')}\n` : '', 'utf8');
  secureFile(queuePath, fsImpl);
  return flushed;
}

export function enqueue(
  event: TrackingEvent,
  queuePath: string,
  fsImpl: NonNullable<SendEventOptions['fsImpl']> = fs,
  maxQueuedEvents: number = DEFAULT_MAX_QUEUED_EVENTS,
): boolean {
  ensureDir(path.dirname(queuePath), fsImpl);
  const max = Math.max(1, maxQueuedEvents);

  let lines: string[] = [];
  if (fsImpl.existsSync(queuePath)) {
    const existing = fsImpl.readFileSync(queuePath, 'utf8');
    lines = existing.split(/\r?\n/).filter((l) => l.trim());
    for (const line of lines) {
      try {
        const parsed = JSON.parse(line) as TrackingEvent;
        if (parsed.externalEventId && parsed.externalEventId === event.externalEventId) {
          return false;
        }
      } catch {
        // continue
      }
    }
  }

  if (lines.length >= max) {
    const over = lines.length - max + 1;
    const dropCount = Math.max(over, Math.max(1, Math.floor(max * 0.1)));
    lines = lines.slice(Math.min(dropCount, lines.length));
    fsImpl.writeFileSync(queuePath, lines.length ? `${lines.join('\n')}\n` : '', 'utf8');
  }

  fsImpl.appendFileSync(queuePath, `${JSON.stringify(event)}\n`, 'utf8');
  secureFile(queuePath, fsImpl);
  return true;
}

function ensureDir(
  dir: string,
  fsImpl: NonNullable<SendEventOptions['fsImpl']>,
): void {
  if (!fsImpl.existsSync(dir)) {
    fsImpl.mkdirSync(dir, { recursive: true });
  }
}

function secureFile(
  filePath: string,
  fsImpl: NonNullable<SendEventOptions['fsImpl']>,
): void {
  try {
    if (process.platform !== 'win32' && fsImpl.chmodSync) {
      fsImpl.chmodSync(filePath, 0o600);
    }
  } catch {
    // best effort
  }
}
