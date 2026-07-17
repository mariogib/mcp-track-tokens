import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { adaptCursorPayload } from '../src/adapters';
import { sanitizeForDiagnostics } from '../src/diagnostics';
import { enqueue, flushQueue, sendEvent } from '../src/send-event';
import { loadConfig, privacySanitize, type TrackingEvent } from '../src/shared';

describe('adapters', () => {
  it('maps common Cursor fields', () => {
    const adapted = adaptCursorPayload(
      {
        workspace_roots: ['/ws/proj'],
        conversation_id: 'conv-1',
        generation_id: 'gen-1',
        model: 'gpt-test',
        prompt: 'secret',
        custom_field: 42,
      },
      'PromptSubmitted',
    );

    expect(adapted.event.eventType).toBe('PromptSubmitted');
    expect(adapted.event.externalConversationId).toBe('conv-1');
    expect(adapted.event.externalRequestId).toBe('gen-1');
    expect(adapted.event.model).toBe('gpt-test');
    expect(adapted.event.promptText).toBe('secret');
    expect(adapted.workspaceRoots).toEqual(['/ws/proj']);
    expect(adapted.event.metadata?.custom_field).toBe(42);
    expect(adapted.event.metadata?.prompt).toBeUndefined();
  });

  it('resolves model_id and nested model objects', () => {
    const fromId = adaptCursorPayload(
      {
        model_id: 'claude-opus-4-7',
        workspace_roots: ['/ws'],
      },
      'PromptSubmitted',
    );
    expect(fromId.event.model).toBe('claude-opus-4-7');
    expect(fromId.event.metadata?.modelId).toBe('claude-opus-4-7');

    const fromNested = adaptCursorPayload(
      {
        model: { slug: 'claude-opus-4-7-thinking-max', id: 'claude-opus-4-7' },
        workspace_roots: ['/ws'],
      },
      'PromptSubmitted',
    );
    expect(fromNested.event.model).toBe('claude-opus-4-7-thinking-max');
  });

  it('accepts camelCase aliases', () => {
    const adapted = adaptCursorPayload(
      {
        workspaceRoots: ['/a'],
        conversationId: 'c',
        generationId: 'g',
        durationMs: 1200,
      },
      'AgentCompleted',
    );
    expect(adapted.event.durationMilliseconds).toBe(1200);
    expect(adapted.event.externalConversationId).toBe('c');
  });

  it('rejects non-object payloads', () => {
    expect(() => adaptCursorPayload([], 'PromptSubmitted')).toThrow(/JSON object/);
    expect(() => adaptCursorPayload('x', 'PromptSubmitted')).toThrow(/JSON object/);
  });
});

describe('privacy', () => {
  it('does not store prompt content by default', () => {
    const config = loadConfig({
      MCP_TRACK_TOKENS_SERVER_URL: 'http://127.0.0.1:5187',
    });
    const result = privacySanitize('hello world', config, 'salt');
    expect(result.promptLength).toBe(11);
    expect(result.promptContent).toBeUndefined();
    expect(result.promptHash).toBeUndefined();
  });

  it('hashes when enabled', () => {
    const config = loadConfig({
      MCP_TRACK_TOKENS_ENABLE_PROMPT_HASHING: 'true',
    });
    const result = privacySanitize('hello', config, 'salt');
    expect(result.promptHash).toMatch(/^[a-f0-9]{64}$/);
  });
});

describe('diagnostics privacy', () => {
  it('redacts prompt fields by default', () => {
    const sanitized = sanitizeForDiagnostics({
      prompt: 'top secret',
      model: 'x',
    }) as Record<string, unknown>;
    expect(String(sanitized.prompt)).toContain('redacted');
    expect(sanitized.model).toBe('x');
  });
});

describe('send-event queue', () => {
  let dir: string;

  afterEach(() => {
    if (dir) {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  function sampleEvent(id: string): TrackingEvent {
    return {
      schemaVersion: '1.0',
      externalEventId: id,
      eventType: 'PromptSubmitted',
      timestampUtc: new Date().toISOString(),
      editor: 'Cursor',
      promptLength: 5,
    };
  }

  it('queues when POST fails and dedupes', async () => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-ch-'));
    const queuePath = path.join(dir, 'cursor-events.jsonl');
    const fetchImpl = vi.fn(async () => {
      throw new Error('offline');
    }) as unknown as typeof fetch;

    const config = {
      serverUrl: 'http://127.0.0.1:5187',
      apiKey: 'k',
      timeoutMs: 200,
      enablePromptHashing: false,
      storePromptContent: false,
      queuePath,
    };

    const result = await sendEvent(sampleEvent('e1'), { config, fetchImpl });
    expect(result.queued).toBe(true);
    expect(fs.readFileSync(queuePath, 'utf8').trim().split('\n')).toHaveLength(1);

    enqueue(sampleEvent('e1'), queuePath);
    expect(fs.readFileSync(queuePath, 'utf8').trim().split('\n')).toHaveLength(1);
  });

  it('retries once then succeeds', async () => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-ch-'));
    const queuePath = path.join(dir, 'cursor-events.jsonl');
    let attempts = 0;
    const fetchImpl = vi.fn(async () => {
      attempts += 1;
      if (attempts === 1) {
        throw new Error('temp');
      }
      return new Response('{}', { status: 200 });
    }) as unknown as typeof fetch;

    const config = {
      serverUrl: 'http://127.0.0.1:5187',
      timeoutMs: 500,
      enablePromptHashing: false,
      storePromptContent: false,
      queuePath,
    };

    const result = await sendEvent(sampleEvent('ok-1'), { config, fetchImpl });
    expect(result.ok).toBe(true);
    expect(attempts).toBe(2);
  });

  it('flushes queued events on later success', async () => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mtt-ch-'));
    const queuePath = path.join(dir, 'cursor-events.jsonl');
    enqueue(sampleEvent('queued-1'), queuePath);

    const fetchImpl = vi.fn(async () => new Response('{}', { status: 200 })) as unknown as typeof fetch;
    const config = {
      serverUrl: 'http://127.0.0.1:5187',
      timeoutMs: 500,
      enablePromptHashing: false,
      storePromptContent: false,
      queuePath,
    };

    const flushed = await flushQueue({ config, fetchImpl });
    expect(flushed).toBe(1);
    expect(fs.readFileSync(queuePath, 'utf8').trim()).toBe('');
  });
});
