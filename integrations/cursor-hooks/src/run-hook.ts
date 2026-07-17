import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { adaptCursorPayload } from './adapters';
import { resolveGit } from './git';
import { sendEventSafe } from './send-event';
import {
  loadConfig,
  privacySanitize,
  readStdinJson,
  type ActivityEventType,
  type TrackingEvent,
} from './shared';

function writeModelDiag(raw: unknown, model: string | undefined, eventType: string): void {
  try {
    if (model || typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
      return;
    }
    const keys = Object.keys(raw as object).sort();
    const record = raw as Record<string, unknown>;
    const line = JSON.stringify({
      at: new Date().toISOString(),
      eventType,
      keys,
      model: record.model ?? null,
      model_id: record.model_id ?? record.modelId ?? null,
    });
    const dir = path.join(os.homedir(), '.mcp-track-tokens', 'logs');
    fs.mkdirSync(dir, { recursive: true });
    fs.appendFileSync(path.join(dir, 'hook-model-diag.jsonl'), `${line}\n`, 'utf8');
  } catch {
    // ignore diagnostics failures
  }
}

/**
 * Shared CLI entry for each Cursor hook script.
 * Always exits 0 unless MCP_TRACK_TOKENS_STRICT_EXIT=1 (fail-safe for Cursor workflow).
 */
export async function runHook(eventType: ActivityEventType): Promise<void> {
  const config = loadConfig();
  try {
    const raw = await readStdinJson();
    const adapted = adaptCursorPayload(raw, eventType);
    writeModelDiag(raw, adapted.event.model, eventType);
    const git = await resolveGit({
      cwd: adapted.event.workspacePath,
      workspaceRoots: adapted.workspaceRoots,
      repositoryPath: adapted.event.repositoryPath,
    });

    const privacy = privacySanitize(
      adapted.event.promptText,
      config,
      adapted.event.externalSessionId ||
        adapted.event.externalConversationId ||
        adapted.event.externalEventId,
    );

    const event: TrackingEvent = {
      schemaVersion: '1.0',
      externalEventId: adapted.event.externalEventId,
      eventType: adapted.event.eventType,
      timestampUtc: new Date().toISOString(),
      editor: 'Cursor',
      editorVersion: adapted.event.editorVersion,
      machineName: os.hostname(),
      userName: os.userInfo().username,
      externalSessionId: adapted.event.externalSessionId,
      externalConversationId: adapted.event.externalConversationId,
      externalRequestId: adapted.event.externalRequestId,
      workspacePath: adapted.event.workspacePath ?? git.repositoryPath,
      repositoryPath: git.repositoryPath ?? adapted.event.repositoryPath,
      remoteUrl: git.remoteUrl ?? adapted.event.remoteUrl,
      branch: git.branch ?? adapted.event.branch,
      activeFilePath: adapted.event.activeFilePath,
      projectId: adapted.event.projectId,
      model: adapted.event.model,
      provider: adapted.event.provider,
      promptLength: privacy.promptLength,
      promptHash: privacy.promptHash,
      promptContent: privacy.promptContent,
      status: adapted.event.status,
      durationMilliseconds: adapted.event.durationMilliseconds,
      responseCompletedAtUtc: adapted.event.responseCompletedAtUtc,
      metadata: adapted.event.metadata,
    };

    await sendEventSafe(event, { config });
  } catch (err) {
    if (process.env.MCP_TRACK_TOKENS_DEBUG === '1') {
      console.error(err);
    }
  }

  if (process.env.MCP_TRACK_TOKENS_STRICT_EXIT === '1') {
    // reserved for diagnostics
  }
  process.exitCode = 0;
}
