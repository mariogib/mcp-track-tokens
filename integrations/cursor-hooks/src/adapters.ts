import { normalizeWorkspacePath } from './git';
import type { ActivityEventType, CursorHookPayload, TrackingEvent } from './shared';
import { newEventId } from './shared';

export interface AdaptedHook {
  event: Omit<TrackingEvent, 'schemaVersion' | 'editor' | 'timestampUtc'> & {
    eventType: ActivityEventType;
    promptText?: string;
  };
  workspaceRoots: string[];
  unknownProps: Record<string, unknown>;
}

const KNOWN_KEYS = new Set([
  'workspace_roots',
  'workspaceRoots',
  'cwd',
  'conversation_id',
  'conversationId',
  'generation_id',
  'generationId',
  'model',
  'model_id',
  'modelId',
  'model_name',
  'modelName',
  'model_params',
  'modelParams',
  'prompt',
  'text',
  'content',
  'status',
  'error',
  'duration_ms',
  'durationMs',
  'session_id',
  'sessionId',
  'repository_path',
  'repositoryPath',
  'branch',
  'remote_url',
  'remoteUrl',
  'editor_version',
  'editorVersion',
  'cursor_version',
  'cursorVersion',
  'active_file',
  'activeFile',
  'active_file_path',
  'activeFilePath',
  'project_id',
  'projectId',
  'provider',
  'timestamp',
  'timestamp_utc',
  'timestampUtc',
  'composer_mode',
  'composerMode',
]);

function asString(value: unknown): string | undefined {
  if (typeof value === 'string' && value.trim()) {
    return value.trim();
  }
  if (typeof value === 'number' && Number.isFinite(value)) {
    return String(value);
  }
  return undefined;
}

function asNumber(value: unknown): number | undefined {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === 'string' && value.trim() && !Number.isNaN(Number(value))) {
    return Number(value);
  }
  return undefined;
}

function asStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.map(asString).filter((v): v is string => Boolean(v));
}

/** Map Cursor stop/hook status strings to tracking ActivityStatus names. */
export function mapCursorStatus(
  status: string | undefined,
  eventType: ActivityEventType,
): string | undefined {
  const normalized = status?.trim().toLowerCase();
  if (normalized === 'completed' || normalized === 'success' || normalized === 'ok') {
    return 'Completed';
  }
  if (
    normalized === 'aborted' ||
    normalized === 'cancelled' ||
    normalized === 'canceled' ||
    normalized === 'stopped'
  ) {
    return 'Cancelled';
  }
  if (normalized === 'error' || normalized === 'failed' || normalized === 'failure') {
    return 'Failed';
  }

  if (eventType === 'AgentCompleted') {
    return 'Completed';
  }
  if (eventType === 'AgentFailed') {
    return 'Failed';
  }
  if (eventType === 'AgentCancelled') {
    return 'Cancelled';
  }

  return status;
}

/**
 * Resolve model from Cursor's evolving payload shapes.
 * Prefers human-readable slug (`model`) then structured ids / nested objects.
 * Cursor Auto mode reports `default`; usage exports often use `Auto`.
 * Canonical stored name is lowercase `auto`.
 */
export function resolveModel(payload: Record<string, unknown>): string | undefined {
  const direct =
    asString(payload.model) ??
    asString(payload.model_id) ??
    asString(payload.modelId) ??
    asString(payload.model_name) ??
    asString(payload.modelName);
  if (direct) {
    return normalizeModelName(direct);
  }

  const nested = payload.model;
  if (nested && typeof nested === 'object' && !Array.isArray(nested)) {
    const obj = nested as Record<string, unknown>;
    const nestedName =
      asString(obj.slug) ??
      asString(obj.name) ??
      asString(obj.id) ??
      asString(obj.model) ??
      asString(obj.modelId) ??
      asString(obj.model_id);
    return nestedName ? normalizeModelName(nestedName) : undefined;
  }

  return undefined;
}

/** Map Cursor aliases to the canonical lowercase usage name. */
export function normalizeModelName(model: string): string {
  const trimmed = model.trim();
  const key = trimmed.toLowerCase();
  return key === 'default' || key === 'auto' ? 'auto' : trimmed;
}

/**
 * Isolate Cursor payload parsing. Preserve unknown props in metadata.
 * Strong validation: requires a plain object; event type is provided by the entry script.
 */
export function adaptCursorPayload(
  raw: unknown,
  eventType: ActivityEventType,
): AdaptedHook {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    throw new Error('Hook payload must be a JSON object');
  }

  const payload = raw as CursorHookPayload;
  const record = payload as Record<string, unknown>;
  const workspaceRoots = [
    ...asStringArray(payload.workspace_roots),
    ...asStringArray(payload.workspaceRoots),
  ]
    .map((root) => normalizeWorkspacePath(root) ?? root)
    .filter(Boolean);
  const cwd = normalizeWorkspacePath(asString(payload.cwd)) ?? asString(payload.cwd);

  const conversationId =
    asString(payload.conversation_id) ?? asString(payload.conversationId);
  const generationId =
    asString(payload.generation_id) ?? asString(payload.generationId);
  const sessionId = asString(payload.session_id) ?? asString(payload.sessionId);
  const model = resolveModel(record);
  const promptText =
    asString(payload.prompt) ?? asString(payload.text) ?? asString(payload.content);
  const status = asString(payload.status);
  const duration =
    asNumber(payload.duration_ms) ?? asNumber(payload.durationMs);
  const repositoryPath = normalizeWorkspacePath(
    asString(payload.repository_path) ?? asString(payload.repositoryPath),
  );
  const branch = asString(payload.branch);
  const remoteUrl = asString(payload.remote_url) ?? asString(payload.remoteUrl);
  const editorVersion =
    asString(payload.editor_version) ??
    asString(payload.editorVersion) ??
    asString(record.cursor_version) ??
    asString(record.cursorVersion);
  const activeFilePath =
    asString(payload.active_file_path) ??
    asString(payload.activeFilePath) ??
    asString(payload.active_file) ??
    asString(payload.activeFile);
  const projectId = asString(payload.project_id) ?? asString(payload.projectId);
  const provider = asString(payload.provider);

  const unknownProps: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(payload)) {
    if (!KNOWN_KEYS.has(key)) {
      unknownProps[key] = value;
    }
  }

  // Never put raw prompt into metadata
  const metadata: Record<string, unknown> = {
    ...unknownProps,
    source: 'cursor-hooks',
  };
  if (payload.error !== undefined) {
    metadata.error = typeof payload.error === 'string' ? payload.error : String(payload.error);
  }
  const modelId = asString(record.model_id) ?? asString(record.modelId);
  if (modelId) {
    metadata.modelId = modelId;
  }
  const modelParams = record.model_params ?? record.modelParams;
  if (modelParams !== undefined) {
    metadata.modelParams = modelParams;
  }
  const composerMode = asString(record.composer_mode) ?? asString(record.composerMode);
  if (composerMode) {
    metadata.composerMode = composerMode;
  }

  // PromptSubmitted uses generationId alone. Completion events must use a distinct
  // externalEventId or the server treats them as duplicates of the prompt row.
  const isTerminal =
    eventType === 'AgentCompleted' ||
    eventType === 'AgentFailed' ||
    eventType === 'AgentCancelled';
  const externalEventId = generationId
    ? isTerminal
      ? `${generationId}:${eventType}`
      : generationId
    : conversationId && eventType
      ? `${conversationId}:${eventType}:${newEventId().slice(0, 8)}`
      : newEventId();

  return {
    workspaceRoots: workspaceRoots.length > 0 ? workspaceRoots : cwd ? [cwd] : [],
    unknownProps,
    event: {
      eventType,
      externalEventId,
      externalConversationId: conversationId,
      externalRequestId: generationId,
      externalSessionId: sessionId,
      model,
      provider,
      status: mapCursorStatus(status, eventType),
      durationMilliseconds: duration,
      repositoryPath,
      branch,
      remoteUrl,
      editorVersion,
      activeFilePath,
      projectId,
      workspacePath: workspaceRoots[0] ?? cwd,
      promptText,
      metadata,
      // Completion time is stamped once in run-hook together with timestampUtc.
      responseCompletedAtUtc: undefined,
    },
  };
}
