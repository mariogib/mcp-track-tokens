import * as vscode from 'vscode';
import type { ExtensionSettings, LogLevel } from './types';

const API_KEY_SECRET = 'mcpTrackTokens.apiKey';

export function readSettings(): ExtensionSettings {
  const cfg = vscode.workspace.getConfiguration('mcpTrackTokens');
  return {
    serverUrl: (cfg.get<string>('serverUrl') || 'http://127.0.0.1:5187').replace(/\/$/, ''),
    autoStartSession: cfg.get<boolean>('autoStartSession', true),
    inactivityThresholdMinutes: cfg.get<number>('inactivityThresholdMinutes', 15),
    enableHeartbeat: cfg.get<boolean>('enableHeartbeat', true),
    heartbeatIntervalMinutes: cfg.get<number>('heartbeatIntervalMinutes', 5),
    enablePromptHashing: cfg.get<boolean>('enablePromptHashing', false),
    storePromptContent: cfg.get<boolean>('storePromptContent', false),
    showStatusBar: cfg.get<boolean>('showStatusBar', true),
    defaultProject: cfg.get<string>('defaultProject', '') || '',
    logLevel: (cfg.get<string>('logLevel', 'info') as LogLevel) || 'info',
  };
}

export class ConfigService {
  constructor(private readonly secrets: vscode.SecretStorage) {}

  getSettings(): ExtensionSettings {
    return readSettings();
  }

  async getApiKey(): Promise<string | undefined> {
    return this.secrets.get(API_KEY_SECRET);
  }

  async setApiKey(key: string): Promise<void> {
    await this.secrets.store(API_KEY_SECRET, key);
  }

  async clearApiKey(): Promise<void> {
    await this.secrets.delete(API_KEY_SECRET);
  }

  async ensureApiKey(): Promise<string | undefined> {
    let key = await this.getApiKey();
    if (key) {
      return key;
    }

    key = await vscode.window.showInputBox({
      prompt: 'Enter your MCP Track Tokens API key',
      password: true,
      ignoreFocusOut: true,
      placeHolder: 'Stored securely in SecretStorage (not settings.json)',
    });

    if (key?.trim()) {
      await this.setApiKey(key.trim());
      return key.trim();
    }

    return undefined;
  }
}
