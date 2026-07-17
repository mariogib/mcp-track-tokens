import * as os from 'os';
import * as path from 'path';
import { createHash, randomUUID } from 'crypto';

export type ActivityEventType =
  | 'PromptSubmitted'
  | 'AgentStarted'
  | 'AgentCompleted'
  | 'AgentCancelled'
  | 'AgentFailed'
  | 'SessionStarted'
  | 'SessionEnded'
  | 'Heartbeat';

export interface HookConfig {
  serverUrl: string;
  apiKey?: string;
  timeoutMs: number;
  enablePromptHashing: boolean;
  storePromptContent: boolean;
  queuePath: string;
}

export interface TrackingEvent {
  schemaVersion: string;
  externalEventId: string;
  eventType: ActivityEventType | string;
  timestampUtc: string;
  editor: string;
  editorVersion?: string;
  machineName?: string;
  userName?: string;
  externalSessionId?: string;
  externalConversationId?: string;
  externalRequestId?: string;
  workspacePath?: string;
  repositoryPath?: string;
  remoteUrl?: string;
  branch?: string;
  activeFilePath?: string;
  projectId?: string;
  model?: string;
  provider?: string;
  promptLength?: number;
  promptHash?: string;
  promptContent?: string;
  status?: string;
  durationMilliseconds?: number;
  responseCompletedAtUtc?: string;
  metadata?: Record<string, unknown>;
}

export interface CursorHookPayload {
  [key: string]: unknown;
  workspace_roots?: string[];
  workspaceRoots?: string[];
  cwd?: string;
  conversation_id?: string;
  conversationId?: string;
  generation_id?: string;
  generationId?: string;
  model?: string;
  prompt?: string;
  text?: string;
  content?: string;
  status?: string;
  error?: string;
  duration_ms?: number;
  durationMs?: number;
  session_id?: string;
  sessionId?: string;
}

export function getQueueDir(): string {
  return path.join(os.homedir(), '.mcp-track-tokens', 'queue');
}

export function getDefaultQueuePath(): string {
  return path.join(getQueueDir(), 'cursor-events.jsonl');
}

export function getDiagnosticsPath(): string {
  return path.join(os.homedir(), '.mcp-track-tokens', 'diagnostics', 'cursor-hook-samples.jsonl');
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): HookConfig {
  const serverUrl = (
    env.MCP_TRACK_TOKENS_SERVER_URL ||
    env.MCP_TRACK_TOKENS_URL ||
    'http://127.0.0.1:5187'
  ).replace(/\/$/, '');

  return {
    serverUrl,
    apiKey: env.MCP_TRACK_TOKENS_API_KEY || env.API_KEY,
    timeoutMs: Math.min(2000, Number(env.MCP_TRACK_TOKENS_TIMEOUT_MS || 2000) || 2000),
    enablePromptHashing: parseBool(env.MCP_TRACK_TOKENS_ENABLE_PROMPT_HASHING, false),
    storePromptContent: parseBool(env.MCP_TRACK_TOKENS_STORE_PROMPT_CONTENT, false),
    queuePath: env.MCP_TRACK_TOKENS_QUEUE_PATH || getDefaultQueuePath(),
  };
}

function parseBool(value: string | undefined, fallback: boolean): boolean {
  if (value === undefined || value === '') {
    return fallback;
  }
  return ['1', 'true', 'yes', 'on'].includes(value.toLowerCase());
}

export function newEventId(): string {
  return randomUUID();
}

export function hashPrompt(text: string, salt: string): string {
  return createHash('sha256').update(`${salt}:${text}`).digest('hex');
}

export async function readStdinJson(): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of process.stdin) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  const raw = Buffer.concat(chunks).toString('utf8').trim();
  if (!raw) {
    return {};
  }
  try {
    return JSON.parse(raw);
  } catch (err) {
    throw new Error(`Invalid JSON on stdin: ${err instanceof Error ? err.message : String(err)}`);
  }
}

export function privacySanitize(
  prompt: string | undefined,
  config: HookConfig,
  salt: string,
): { promptLength?: number; promptHash?: string; promptContent?: string } {
  if (prompt === undefined) {
    return {};
  }
  const result: { promptLength?: number; promptHash?: string; promptContent?: string } = {
    promptLength: prompt.length,
  };
  if (config.enablePromptHashing && prompt.length > 0) {
    result.promptHash = hashPrompt(prompt, salt);
  }
  if (config.storePromptContent && prompt.length > 0) {
    result.promptContent = prompt;
  }
  return result;
}
