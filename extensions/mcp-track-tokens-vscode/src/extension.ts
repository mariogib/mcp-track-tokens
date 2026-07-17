import { randomUUID } from 'crypto';
import * as vscode from 'vscode';
import { ApiClient } from './apiClient';
import { registerChatParticipant } from './chatParticipant';
import { registerCommands } from './commands';
import { ConfigService } from './config';
import { GitResolver } from './gitResolver';
import { OfflineQueue } from './offlineQueue';
import { SessionManager } from './sessionManager';
import { StatusBarController } from './statusBar';

let sessions: SessionManager | undefined;
let statusBar: StatusBarController | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  const config = new ConfigService(context.secrets);
  const settings = config.getSettings();

  const rememberedKey = 'mcpTrackTokens.repoByWorkspace';
  const lastRepoKey = 'mcpTrackTokens.lastSelectedRepo';
  const projectIdKey = 'mcpTrackTokens.currentProjectId';
  const projectNameKey = 'mcpTrackTokens.currentProjectName';

  const queue = new OfflineQueue({
    onDrop: (dropped) => {
      void vscode.window.showWarningMessage(
        `MCP Track Tokens: offline queue full — dropped ${dropped} oldest event(s).`,
      );
    },
  });

  const api = new ApiClient({
    getServerUrl: () => config.getSettings().serverUrl,
    getApiKey: () => config.getApiKey(),
    queue,
    onServerReachable: (ok) => {
      sessions?.setServerOnline(ok);
    },
  });

  const git = new GitResolver({
    getWorkspaceFolders: () => vscode.workspace.workspaceFolders,
    getActiveFilePath: () => vscode.window.activeTextEditor?.document.uri.fsPath,
    getRememberedRepo: (workspacePath) => {
      const map = context.workspaceState.get<Record<string, string>>(rememberedKey, {});
      return map[workspacePath];
    },
    setRememberedRepo: (workspacePath, repositoryPath) => {
      const map = {
        ...context.workspaceState.get<Record<string, string>>(rememberedKey, {}),
        [workspacePath]: repositoryPath,
      };
      void context.workspaceState.update(rememberedKey, map);
    },
    getLastSelectedRepo: () => context.workspaceState.get<string>(lastRepoKey),
    setLastSelectedRepo: (repositoryPath) => {
      void context.workspaceState.update(lastRepoKey, repositoryPath);
    },
    askUserToPickRepo: async (candidates) => {
      const picked = await vscode.window.showQuickPick(
        candidates.map((c) => ({ label: c, description: 'Git repository' })),
        {
          placeHolder: 'Select the repository to track for this workspace',
          ignoreFocusOut: true,
        },
      );
      return picked?.label;
    },
    getGitApiRepos: async () => {
      try {
        const ext = vscode.extensions.getExtension('vscode.git');
        if (!ext) {
          return undefined;
        }
        if (!ext.isActive) {
          await ext.activate();
        }
        const exports = ext.exports as { getAPI?: (version: number) => unknown } | undefined;
        const api = exports?.getAPI?.(1) as
          | {
              repositories: Array<{
                rootUri: { fsPath: string };
                state: {
                  HEAD?: { name?: string } | null;
                  remotes: Array<{ name: string; fetchUrl?: string; pushUrl?: string }>;
                };
              }>;
            }
          | undefined;
        return api;
      } catch {
        return undefined;
      }
    },
  });

  let currentProjectId = context.workspaceState.get<string>(projectIdKey);
  let currentProjectName =
    context.workspaceState.get<string>(projectNameKey) || 'No project';

  statusBar = new StatusBarController();
  statusBar.setVisible(settings.showStatusBar);
  statusBar.update('Paused', currentProjectName);
  context.subscriptions.push(statusBar);

  sessions = new SessionManager({
    api,
    git,
    getSettings: () => config.getSettings(),
    getEditorVersion: () => vscode.version,
    getExternalSessionId: () => {
      const existing = context.workspaceState.get<string>('mcpTrackTokens.externalSessionId');
      if (existing) {
        return existing;
      }
      const id = randomUUID();
      void context.workspaceState.update('mcpTrackTokens.externalSessionId', id);
      return id;
    },
    getProjectId: () => currentProjectId || config.getSettings().defaultProject || undefined,
    getProjectName: () => currentProjectName,
    onStateChange: (state, projectName) => {
      statusBar?.update(state, projectName);
    },
  });
  context.subscriptions.push({ dispose: () => sessions?.dispose() });

  registerCommands(context, {
    api,
    config,
    git,
    sessions,
    queue,
    getProjectId: () => currentProjectId,
    setProjectId: (id) => {
      currentProjectId = id;
      void context.workspaceState.update(projectIdKey, id);
    },
    setProjectName: (name) => {
      currentProjectName = name;
      void context.workspaceState.update(projectNameKey, name);
    },
  });

  context.subscriptions.push(
    registerChatParticipant(context, {
      api,
      config,
      git,
      sessions,
      getEditorVersion: () => vscode.version,
    }),
  );

  context.subscriptions.push(
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('mcpTrackTokens.showStatusBar')) {
        statusBar?.setVisible(config.getSettings().showStatusBar);
      }
    }),
  );

  // Best-effort flush of offline queue on startup
  void api.flushQueue().catch(() => undefined);
}

export async function deactivate(): Promise<void> {
  if (sessions) {
    await sessions.stop('deactivate').catch(() => undefined);
    sessions.dispose();
    sessions = undefined;
  }
  statusBar = undefined;
}
