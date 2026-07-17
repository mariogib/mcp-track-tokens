import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import type { IngestEvent } from './types';

export const DEFAULT_MAX_QUEUE_SIZE = 5000;

export function getDefaultQueuePath(): string {
  return path.join(os.homedir(), '.mcp-track-tokens', 'queue', 'vscode-events.jsonl');
}

export interface OfflineQueueOptions {
  filePath?: string;
  maxSize?: number;
  onDrop?: (dropped: number) => void;
  fsImpl?: Pick<typeof fs, 'existsSync' | 'mkdirSync' | 'readFileSync' | 'writeFileSync' | 'appendFileSync'>;
}

/**
 * JSONL offline queue with dedupe by externalEventId and max size.
 */
export class OfflineQueue {
  private readonly filePath: string;
  private readonly maxSize: number;
  private readonly onDrop?: (dropped: number) => void;
  private readonly fsImpl: NonNullable<OfflineQueueOptions['fsImpl']>;
  private knownIds = new Set<string>();
  private loaded = false;

  constructor(options: OfflineQueueOptions = {}) {
    this.filePath = options.filePath ?? getDefaultQueuePath();
    this.maxSize = options.maxSize ?? DEFAULT_MAX_QUEUE_SIZE;
    this.onDrop = options.onDrop;
    this.fsImpl = options.fsImpl ?? fs;
  }

  get path(): string {
    return this.filePath;
  }

  private ensureLoaded(): void {
    if (this.loaded) {
      return;
    }
    this.loaded = true;
    this.knownIds = new Set();
    if (!this.fsImpl.existsSync(this.filePath)) {
      return;
    }
    const raw = this.fsImpl.readFileSync(this.filePath, 'utf8');
    for (const line of raw.split(/\r?\n/)) {
      if (!line.trim()) continue;
      try {
        const evt = JSON.parse(line) as IngestEvent;
        if (evt.externalEventId) {
          this.knownIds.add(evt.externalEventId);
        }
      } catch {
        // skip corrupt lines
      }
    }
  }

  private ensureDir(): void {
    const dir = path.dirname(this.filePath);
    if (!this.fsImpl.existsSync(dir)) {
      this.fsImpl.mkdirSync(dir, { recursive: true });
    }
  }

  enqueue(event: IngestEvent): boolean {
    this.ensureLoaded();
    if (event.externalEventId && this.knownIds.has(event.externalEventId)) {
      return false;
    }

    const events = this.readAll();
    if (events.length >= this.maxSize) {
      const dropCount = Math.max(1, Math.floor(this.maxSize * 0.1));
      const dropped = events.splice(0, dropCount);
      for (const d of dropped) {
        if (d.externalEventId) {
          this.knownIds.delete(d.externalEventId);
        }
      }
      this.onDrop?.(dropped.length);
      this.rewrite(events);
    }

    this.ensureDir();
    this.fsImpl.appendFileSync(this.filePath, `${JSON.stringify(event)}\n`, 'utf8');
    if (event.externalEventId) {
      this.knownIds.add(event.externalEventId);
    }
    return true;
  }

  readAll(): IngestEvent[] {
    this.ensureLoaded();
    if (!this.fsImpl.existsSync(this.filePath)) {
      return [];
    }
    const raw = this.fsImpl.readFileSync(this.filePath, 'utf8');
    const events: IngestEvent[] = [];
    for (const line of raw.split(/\r?\n/)) {
      if (!line.trim()) continue;
      try {
        events.push(JSON.parse(line) as IngestEvent);
      } catch {
        // skip
      }
    }
    return events;
  }

  size(): number {
    return this.readAll().length;
  }

  clear(): void {
    this.ensureDir();
    this.fsImpl.writeFileSync(this.filePath, '', 'utf8');
    this.knownIds.clear();
    this.loaded = true;
  }

  replaceAll(events: IngestEvent[]): void {
    this.rewrite(events);
  }

  private rewrite(events: IngestEvent[]): void {
    this.ensureDir();
    const body = events.map((e) => JSON.stringify(e)).join('\n');
    this.fsImpl.writeFileSync(this.filePath, body ? `${body}\n` : '', 'utf8');
    this.knownIds = new Set(
      events.map((e) => e.externalEventId).filter((id): id is string => Boolean(id)),
    );
    this.loaded = true;
  }
}
