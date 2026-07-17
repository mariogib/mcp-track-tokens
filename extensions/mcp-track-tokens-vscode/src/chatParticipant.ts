import { randomUUID } from 'crypto';
import * as vscode from 'vscode';
import type { ApiClient } from './apiClient';
import type { ConfigService } from './config';
import type { GitResolver } from './gitResolver';
import { sanitizePrompt } from './privacy';
import type { SessionManager } from './sessionManager';

export interface ChatParticipantDeps {
  api: ApiClient;
  config: ConfigService;
  git: GitResolver;
  sessions: SessionManager;
  getEditorVersion: () => string;
}

/**
 * @track chat participant: records PromptSubmitted (no content by default),
 * invokes vscode.lm language models, streams the response, records complete/cancel/fail.
 */
export function registerChatParticipant(
  context: vscode.ExtensionContext,
  deps: ChatParticipantDeps,
): vscode.Disposable {
  const participant = vscode.chat.createChatParticipant(
    'mabatar.mcp-track-tokens.track',
    async (request, _context, stream, token) => {
      const settings = deps.config.getSettings();
      const prompt = request.prompt ?? '';
      const privacy = sanitizePrompt(prompt, {
        enablePromptHashing: settings.enablePromptHashing,
        storePromptContent: settings.storePromptContent,
        hashSalt: deps.sessions.getSessionId() ?? deps.sessions.getExternalSessionId(),
      });

      if (settings.autoStartSession && deps.sessions.getState() !== 'Tracking') {
        await deps.sessions.start('prompt');
      }
      deps.sessions.noteActivity();

      const repo = await deps.git.resolve();
      const externalEventId = randomUUID();
      const startedAt = Date.now();

      await deps.api.postEventSafe({
        schemaVersion: '1.0',
        externalEventId,
        eventType: 'PromptSubmitted',
        timestampUtc: new Date(startedAt).toISOString(),
        editor: 'VisualStudioCode',
        editorVersion: deps.getEditorVersion(),
        externalSessionId: deps.sessions.getExternalSessionId(),
        externalRequestId: externalEventId,
        workspacePath: repo.workspacePath,
        repositoryPath: repo.repositoryPath,
        remoteUrl: repo.remoteUrl,
        branch: repo.branch,
        activeFilePath: repo.activeFilePath,
        projectId: settings.defaultProject || undefined,
        promptLength: privacy.promptLength,
        promptHash: privacy.promptHash,
        promptContent: privacy.promptContent,
        status: 'Started',
        metadata: { source: 'chatParticipant', participant: 'track' },
      });

      await deps.api.postEventSafe({
        schemaVersion: '1.0',
        externalEventId: `${externalEventId}:started`,
        eventType: 'AgentStarted',
        timestampUtc: new Date().toISOString(),
        editor: 'VisualStudioCode',
        editorVersion: deps.getEditorVersion(),
        externalSessionId: deps.sessions.getExternalSessionId(),
        externalRequestId: externalEventId,
        workspacePath: repo.workspacePath,
        repositoryPath: repo.repositoryPath,
        remoteUrl: repo.remoteUrl,
        branch: repo.branch,
        status: 'Running',
        metadata: { source: 'chatParticipant' },
      });

      let model: vscode.LanguageModelChat | undefined;
      try {
        const models = await vscode.lm.selectChatModels({
          vendor: 'copilot',
        });
        model = models[0];
        if (!model) {
          const anyModels = await vscode.lm.selectChatModels({});
          model = anyModels[0];
        }
      } catch {
        model = undefined;
      }

      if (!model) {
        stream.markdown(
          'No language model is available. Install/enable a chat model provider (e.g. GitHub Copilot).',
        );
        await deps.api.postEventSafe({
          schemaVersion: '1.0',
          externalEventId: `${externalEventId}:failed`,
          eventType: 'AgentFailed',
          timestampUtc: new Date().toISOString(),
          editor: 'VisualStudioCode',
          externalSessionId: deps.sessions.getExternalSessionId(),
          externalRequestId: externalEventId,
          workspacePath: repo.workspacePath,
          repositoryPath: repo.repositoryPath,
          status: 'Failed',
          durationMilliseconds: Date.now() - startedAt,
          metadata: { reason: 'no-model' },
        });
        return;
      }

      const messages = [
        vscode.LanguageModelChatMessage.User(prompt),
      ];

      try {
        const chatResponse = await model.sendRequest(messages, {}, token);
        for await (const fragment of chatResponse.text) {
          if (token.isCancellationRequested) {
            break;
          }
          stream.markdown(fragment);
        }

        if (token.isCancellationRequested) {
          await deps.api.postEventSafe({
            schemaVersion: '1.0',
            externalEventId: `${externalEventId}:cancelled`,
            eventType: 'AgentCancelled',
            timestampUtc: new Date().toISOString(),
            editor: 'VisualStudioCode',
            externalSessionId: deps.sessions.getExternalSessionId(),
            externalRequestId: externalEventId,
            workspacePath: repo.workspacePath,
            repositoryPath: repo.repositoryPath,
            model: model.name,
            status: 'Cancelled',
            durationMilliseconds: Date.now() - startedAt,
            responseCompletedAtUtc: new Date().toISOString(),
          });
          return;
        }

        await deps.api.postEventSafe({
          schemaVersion: '1.0',
          externalEventId: `${externalEventId}:completed`,
          eventType: 'AgentCompleted',
          timestampUtc: new Date().toISOString(),
          editor: 'VisualStudioCode',
          externalSessionId: deps.sessions.getExternalSessionId(),
          externalRequestId: externalEventId,
          workspacePath: repo.workspacePath,
          repositoryPath: repo.repositoryPath,
          remoteUrl: repo.remoteUrl,
          branch: repo.branch,
          model: model.name,
          status: 'Completed',
          durationMilliseconds: Date.now() - startedAt,
          responseCompletedAtUtc: new Date().toISOString(),
        });
      } catch (err) {
        if (token.isCancellationRequested) {
          await deps.api.postEventSafe({
            schemaVersion: '1.0',
            externalEventId: `${externalEventId}:cancelled`,
            eventType: 'AgentCancelled',
            timestampUtc: new Date().toISOString(),
            editor: 'VisualStudioCode',
            externalSessionId: deps.sessions.getExternalSessionId(),
            externalRequestId: externalEventId,
            model: model.name,
            status: 'Cancelled',
            durationMilliseconds: Date.now() - startedAt,
          });
          return;
        }

        const message = err instanceof Error ? err.message : String(err);
        stream.markdown(`\n\n_Request failed: ${message}_`);
        await deps.api.postEventSafe({
          schemaVersion: '1.0',
          externalEventId: `${externalEventId}:failed`,
          eventType: 'AgentFailed',
          timestampUtc: new Date().toISOString(),
          editor: 'VisualStudioCode',
          externalSessionId: deps.sessions.getExternalSessionId(),
          externalRequestId: externalEventId,
          model: model.name,
          status: 'Failed',
          durationMilliseconds: Date.now() - startedAt,
          metadata: { error: message },
        });
      }
    },
  );

  participant.iconPath = vscode.Uri.joinPath(context.extensionUri, 'media', 'icon.svg');
  return participant;
}
