import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../../src/apiClient';
import { OfflineQueue } from '../../src/offlineQueue';
import { sanitizePrompt, assertNoPromptLeak } from '../../src/privacy';
import { selectRepositoryPath, findNearestGitRoot } from '../../src/gitResolver';
import {
  formatStatusBarText,
  resolveStatusBarState,
} from '../../src/statusBarUi';
import { SessionManager } from '../../src/sessionManager';
import type { ExtensionSettings, IngestEvent } from '../../src/types';

describe('selectRepositoryPath', () => {
  it('prefers the active file repository', () => {
    const result = selectRepositoryPath({
      candidates: ['C:/repos/a', 'C:/repos/b'],
      activeFilePath: 'C:/repos/b/src/index.ts',
      lastSelected: 'C:/repos/a',
    });
    expect(result.repositoryPath?.replace(/\\/g, '/').toLowerCase()).toContain('repos/b');
    expect(result.needsAsk).toBe(false);
  });

  it('uses last selected when no active file match', () => {
    const result = selectRepositoryPath({
      candidates: ['/repos/a', '/repos/b'],
      lastSelected: '/repos/a',
    });
    expect(result.repositoryPath).toBe('/repos/a');
    expect(result.needsAsk).toBe(false);
  });

  it('asks when multiple repos and no preference', () => {
    const result = selectRepositoryPath({
      candidates: ['/repos/a', '/repos/b'],
    });
    expect(result.needsAsk).toBe(true);
    expect(result.repositoryPath).toBeUndefined();
  });

  it('returns single candidate without asking', () => {
    const result = selectRepositoryPath({ candidates: ['/only'] });
    expect(result.repositoryPath).toBe('/only');
    expect(result.needsAsk).toBe(false);
  });
});

describe('findNearestGitRoot', () => {
  it('walks up to .git', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-git-'));
    const nested = path.join(root, 'a', 'b');
    fs.mkdirSync(nested, { recursive: true });
    fs.mkdirSync(path.join(root, '.git'));
    expect(findNearestGitRoot(nested)).toBe(root);
    fs.rmSync(root, { recursive: true, force: true });
  });
});

describe('OfflineQueue', () => {
  let dir: string;
  let file: string;

  afterEach(() => {
    if (dir) {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  function makeQueue(maxSize = 100) {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-q-'));
    file = path.join(dir, 'vscode-events.jsonl');
    return new OfflineQueue({ filePath: file, maxSize });
  }

  it('enqueues and reads JSONL events', () => {
    const q = makeQueue();
    const evt: IngestEvent = {
      externalEventId: 'e1',
      eventType: 'PromptSubmitted',
      timestampUtc: new Date().toISOString(),
      editor: 'VisualStudioCode',
    };
    expect(q.enqueue(evt)).toBe(true);
    expect(q.size()).toBe(1);
    expect(q.readAll()[0].externalEventId).toBe('e1');
  });

  it('deduplicates by externalEventId', () => {
    const q = makeQueue();
    const evt: IngestEvent = {
      externalEventId: 'dup',
      eventType: 'PromptSubmitted',
      timestampUtc: new Date().toISOString(),
      editor: 'VisualStudioCode',
    };
    expect(q.enqueue(evt)).toBe(true);
    expect(q.enqueue({ ...evt })).toBe(false);
    expect(q.size()).toBe(1);
  });

  it('drops oldest when max size exceeded and warns', () => {
    const drops: number[] = [];
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-q-'));
    file = path.join(dir, 'vscode-events.jsonl');
    const q = new OfflineQueue({
      filePath: file,
      maxSize: 5,
      onDrop: (n) => drops.push(n),
    });
    for (let i = 0; i < 5; i++) {
      q.enqueue({
        externalEventId: `e${i}`,
        eventType: 'Heartbeat',
        timestampUtc: new Date().toISOString(),
        editor: 'VisualStudioCode',
      });
    }
    q.enqueue({
      externalEventId: 'e5',
      eventType: 'Heartbeat',
      timestampUtc: new Date().toISOString(),
      editor: 'VisualStudioCode',
    });
    expect(drops.length).toBe(1);
    expect(drops[0]).toBeGreaterThan(0);
    expect(q.size()).toBeLessThanOrEqual(5);
  });
});

describe('privacy', () => {
  it('never includes prompt content by default', () => {
    const result = sanitizePrompt('secret prompt text', {
      enablePromptHashing: false,
      storePromptContent: false,
    });
    expect(result.promptLength).toBe('secret prompt text'.length);
    expect(result.promptContent).toBeUndefined();
    expect(result.promptHash).toBeUndefined();
  });

  it('hashes when enabled', () => {
    const result = sanitizePrompt('hello', {
      enablePromptHashing: true,
      storePromptContent: false,
      hashSalt: 'salt',
    });
    expect(result.promptHash).toMatch(/^[a-f0-9]{64}$/);
    expect(result.promptContent).toBeUndefined();
  });

  it('stores content only when enabled', () => {
    const result = sanitizePrompt('hello', {
      enablePromptHashing: false,
      storePromptContent: true,
    });
    expect(result.promptContent).toBe('hello');
  });

  it('assertNoPromptLeak throws when content leaks', () => {
    expect(() =>
      assertNoPromptLeak({ promptContent: 'x' }, false),
    ).toThrow(/Privacy violation/);
  });
});

describe('status bar state', () => {
  it('formats text', () => {
    expect(formatStatusBarText({ state: 'Tracking', projectName: 'Acme' })).toBe(
      '$(record) Track: Acme',
    );
  });

  it('resolves states', () => {
    expect(
      resolveStatusBarState({
        tracking: true,
        paused: false,
        hasProject: true,
        serverOnline: true,
      }),
    ).toBe('Tracking');
    expect(
      resolveStatusBarState({
        tracking: true,
        paused: false,
        hasProject: false,
        serverOnline: true,
      }),
    ).toBe('Unallocated');
    expect(
      resolveStatusBarState({
        tracking: false,
        paused: true,
        hasProject: true,
        serverOnline: true,
      }),
    ).toBe('Paused');
    expect(
      resolveStatusBarState({
        tracking: true,
        paused: false,
        hasProject: true,
        serverOnline: false,
      }),
    ).toBe('Server Offline');
  });
});

describe('ApiClient retries', () => {
  it('retries on failure then succeeds', async () => {
    let attempts = 0;
    const fetchImpl = vi.fn(async () => {
      attempts += 1;
      if (attempts < 3) {
        throw new Error('network');
      }
      return new Response(JSON.stringify({ eventId: '1', wasDuplicate: false }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as unknown as typeof fetch;

    const client = new ApiClient({
      getServerUrl: () => 'http://127.0.0.1:5187',
      getApiKey: async () => 'key',
      fetchImpl,
      maxRetries: 2,
      timeoutMs: 1000,
    });

    const result = await client.postEvent({
      eventType: 'PromptSubmitted',
      timestampUtc: new Date().toISOString(),
      editor: 'VisualStudioCode',
      externalEventId: 'retry-1',
    });

    expect(result?.eventId).toBe('1');
    expect(attempts).toBe(3);
  });

  it('queues when all retries fail', async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-api-'));
    const file = path.join(dir, 'q.jsonl');
    const queue = new OfflineQueue({ filePath: file });
    const fetchImpl = vi.fn(async () => {
      throw new Error('down');
    }) as unknown as typeof fetch;

    const client = new ApiClient({
      getServerUrl: () => 'http://127.0.0.1:5187',
      getApiKey: async () => 'key',
      fetchImpl,
      queue,
      maxRetries: 1,
      timeoutMs: 200,
    });

    await expect(
      client.postEvent({
        eventType: 'PromptSubmitted',
        timestampUtc: new Date().toISOString(),
        editor: 'VisualStudioCode',
        externalEventId: 'offline-1',
      }),
    ).rejects.toThrow();
    expect(queue.size()).toBe(1);
    fs.rmSync(dir, { recursive: true, force: true });
  });
});

describe('SessionManager inactivity', () => {
  it('pauses after inactivity threshold', async () => {
    let now = 1_000_000;
    const settings: ExtensionSettings = {
      serverUrl: 'http://127.0.0.1:5187',
      autoStartSession: true,
      inactivityThresholdMinutes: 15,
      enableHeartbeat: false,
      heartbeatIntervalMinutes: 5,
      enablePromptHashing: false,
      storePromptContent: false,
      showStatusBar: true,
      defaultProject: '11111111-1111-1111-1111-111111111111',
      logLevel: 'info',
    };

    const api = {
      startSession: vi.fn(async () => ({ id: 'sess-1' })),
      endSession: vi.fn(async () => ({})),
      heartbeat: vi.fn(async () => ({})),
    };

    const git = {
      resolve: vi.fn(async () => ({
        workspacePath: '/ws',
        repositoryPath: '/ws/repo',
        branch: 'main',
      })),
    };

    const manager = new SessionManager({
      api: api as any,
      git: git as any,
      getSettings: () => settings,
      getEditorVersion: () => '1.85.0',
      getExternalSessionId: () => 'ext-1',
      getProjectId: () => settings.defaultProject,
      getProjectName: () => 'Demo',
      setIntervalFn: (() => 0) as any,
      clearIntervalFn: (() => undefined) as any,
      now: () => now,
    });

    await manager.start('test');
    expect(manager.getState()).toBe('Tracking');

    now += 14 * 60_000;
    manager.checkInactivity();
    expect(manager.getState()).toBe('Tracking');

    now += 2 * 60_000;
    manager.checkInactivity();
    expect(manager.getState()).toBe('Paused');
  });
});
