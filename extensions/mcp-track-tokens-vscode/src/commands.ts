import * as vscode from 'vscode';
import type { ApiClient } from './apiClient';
import type { ConfigService } from './config';
import type { GitResolver } from './gitResolver';
import type { OfflineQueue } from './offlineQueue';
import type { SessionManager } from './sessionManager';

export interface CommandsDeps {
  api: ApiClient;
  config: ConfigService;
  git: GitResolver;
  sessions: SessionManager;
  queue: OfflineQueue;
  getProjectId: () => string | undefined;
  setProjectId: (id: string | undefined) => void;
  setProjectName: (name: string) => void;
}

export function registerCommands(
  context: vscode.ExtensionContext,
  deps: CommandsDeps,
): void {
  const register = (command: string, handler: () => Promise<void>) => {
    context.subscriptions.push(
      vscode.commands.registerCommand(command, () => {
        void handler().catch((err) => {
          const message = err instanceof Error ? err.message : String(err);
          void vscode.window.showErrorMessage(`MCP Track Tokens: ${message}`);
        });
      }),
    );
  };

  register('mcpTrackTokens.registerProject', async () => {
    await deps.config.ensureApiKey();
    const repo = await deps.git.resolve();
    const name = await vscode.window.showInputBox({
      prompt: 'Project name',
      value: repo.repositoryPath
        ? basename(repo.repositoryPath)
        : vscode.workspace.name || 'Untitled Project',
      ignoreFocusOut: true,
    });
    if (!name) {
      return;
    }

    const project = await deps.api.registerProject({
      name,
      repositoryPath: repo.repositoryPath,
      remoteUrl: repo.remoteUrl,
    });
    deps.setProjectId(project.id);
    deps.setProjectName(project.name);
    deps.sessions.setProjectName(project.name);
    void vscode.window.showInformationMessage(
      `Registered project "${project.name}" (${project.id}).`,
    );
  });

  register('mcpTrackTokens.startSession', async () => {
    await deps.config.ensureApiKey();
    await deps.sessions.start('manual');
    void vscode.window.showInformationMessage('Tracking session started.');
  });

  register('mcpTrackTokens.stopSession', async () => {
    await deps.sessions.stop('manual');
    void vscode.window.showInformationMessage('Tracking session stopped.');
  });

  register('mcpTrackTokens.showStatus', async () => {
    const settings = deps.config.getSettings();
    let serverLine = 'unknown';
    try {
      const health = await deps.api.testConnection();
      serverLine = health.ok ? 'online' : `offline (${health.message})`;
    } catch (err) {
      serverLine = err instanceof Error ? err.message : 'offline';
    }

    const repo = await deps.git.resolve();
    const lines = [
      `State: ${deps.sessions.getState()}`,
      `Project: ${deps.sessions.getProjectName()}`,
      `Project ID: ${deps.getProjectId() || settings.defaultProject || '(none)'}`,
      `Session: ${deps.sessions.getSessionId() || '(none)'}`,
      `External session: ${deps.sessions.getExternalSessionId()}`,
      `Server: ${serverLine}`,
      `Queue: ${deps.queue.size()} event(s)`,
      `Repository: ${repo.repositoryPath || '(none)'}`,
      `Branch: ${repo.branch || '(none)'}`,
      `Remote: ${repo.remoteUrl || '(none)'}`,
    ];
    void vscode.window.showInformationMessage(lines.join('\n'), { modal: true });
  });

  register('mcpTrackTokens.openDashboard', async () => {
    const settings = deps.config.getSettings();
    const url = `${settings.serverUrl.replace(/\/$/, '')}/`;
    await vscode.env.openExternal(vscode.Uri.parse(url));
  });

  register('mcpTrackTokens.assignUnallocated', async () => {
    await deps.config.ensureApiKey();
    try {
      const data = await deps.api.getUnallocated();
      const text = JSON.stringify(data, null, 2);
      const doc = await vscode.workspace.openTextDocument({
        content: text,
        language: 'json',
      });
      await vscode.window.showTextDocument(doc);
      void vscode.window.showInformationMessage(
        'Opened unallocated activity. Assign via the dashboard or MCP tools.',
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      void vscode.window.showErrorMessage(`Could not load unallocated activity: ${message}`);
    }
  });

  register('mcpTrackTokens.testConnection', async () => {
    await deps.config.ensureApiKey();
    const result = await deps.api.testConnection();
    if (result.ok) {
      const flushed = await deps.api.flushQueue();
      void vscode.window.showInformationMessage(
        `${result.message}${flushed ? ` Flushed ${flushed} queued event(s).` : ''}`,
      );
    } else {
      void vscode.window.showWarningMessage(`Connection failed: ${result.message}`);
    }
  });

  register('mcpTrackTokens.copyRepoInfo', async () => {
    const repo = await deps.git.resolve();
    const payload = JSON.stringify(repo, null, 2);
    await vscode.env.clipboard.writeText(payload);
    void vscode.window.showInformationMessage('Repository information copied to clipboard.');
  });
}

function basename(p: string): string {
  const parts = p.replace(/\\/g, '/').split('/');
  return parts[parts.length - 1] || p;
}
