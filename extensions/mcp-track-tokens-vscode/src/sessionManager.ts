import type { ApiClient } from './apiClient';
import type { GitResolver } from './gitResolver';
import type { ExtensionSettings, TrackingState } from './types';

export interface SessionManagerDeps {
  api: ApiClient;
  git: GitResolver;
  getSettings: () => ExtensionSettings;
  getEditorVersion: () => string;
  getExternalSessionId: () => string;
  getProjectId?: () => string | undefined;
  getProjectName?: () => string | undefined;
  onStateChange?: (state: TrackingState, projectName: string) => void;
  setIntervalFn?: typeof setInterval;
  clearIntervalFn?: typeof clearInterval;
  now?: () => number;
}

/**
 * Manages start/stop/pause, inactivity timeout, and heartbeats.
 */
export class SessionManager {
  private state: TrackingState = 'Paused';
  private projectName = 'No project';
  private serverOnline = true;
  private sessionId?: string;
  private externalSessionId: string;
  private lastActivityAt = 0;
  private inactivityTimer?: ReturnType<typeof setInterval>;
  private heartbeatTimer?: ReturnType<typeof setInterval>;
  private readonly setIntervalFn: typeof setInterval;
  private readonly clearIntervalFn: typeof clearInterval;
  private readonly now: () => number;

  constructor(private readonly deps: SessionManagerDeps) {
    this.externalSessionId = deps.getExternalSessionId();
    this.setIntervalFn = deps.setIntervalFn ?? setInterval;
    this.clearIntervalFn = deps.clearIntervalFn ?? clearInterval;
    this.now = deps.now ?? (() => Date.now());
  }

  getState(): TrackingState {
    return this.state;
  }

  getProjectName(): string {
    return this.projectName;
  }

  getSessionId(): string | undefined {
    return this.sessionId;
  }

  getExternalSessionId(): string {
    return this.externalSessionId;
  }

  setServerOnline(online: boolean): void {
    this.serverOnline = online;
    this.emit();
  }

  setProjectName(name: string): void {
    this.projectName = name || 'No project';
    this.emit();
  }

  noteActivity(): void {
    this.lastActivityAt = this.now();
    if (this.state === 'Paused' && this.sessionId) {
      this.state = 'Tracking';
      this.emit();
    }
  }

  async start(reason = 'manual'): Promise<void> {
    const settings = this.deps.getSettings();
    const repo = await this.deps.git.resolve();
    const projectId = this.deps.getProjectId?.() || settings.defaultProject || undefined;

    try {
      const result = await this.deps.api.startSession({
        projectId: projectId || undefined,
        editor: 'VisualStudioCode',
        editorVersion: this.deps.getEditorVersion(),
        workspacePath: repo.workspacePath,
        repositoryPath: repo.repositoryPath,
        remoteUrl: repo.remoteUrl,
        branch: repo.branch,
        externalSessionId: this.externalSessionId,
        notes: `start:${reason}`,
        startedAtUtc: new Date(this.now()).toISOString(),
      });
      this.sessionId = result?.id;
      this.serverOnline = true;
    } catch {
      this.serverOnline = false;
    }

    this.lastActivityAt = this.now();
    this.state = this.serverOnline
      ? projectId || repo.repositoryPath
        ? 'Tracking'
        : 'Unallocated'
      : 'Server Offline';

    if (this.deps.getProjectName?.()) {
      this.projectName = this.deps.getProjectName()!;
    } else if (repo.repositoryPath) {
      this.projectName = basename(repo.repositoryPath);
    }

    this.armTimers(settings);
    this.emit();
  }

  async stop(reason = 'manual'): Promise<void> {
    this.clearTimers();
    try {
      await this.deps.api.endSession({
        sessionId: this.sessionId,
        externalSessionId: this.externalSessionId,
        editor: 'VisualStudioCode',
        notes: `stop:${reason}`,
        endedAtUtc: new Date(this.now()).toISOString(),
      });
      this.serverOnline = true;
    } catch {
      this.serverOnline = false;
    }
    this.sessionId = undefined;
    this.state = this.serverOnline ? 'Paused' : 'Server Offline';
    this.emit();
  }

  pause(reason = 'inactivity'): void {
    this.state = 'Paused';
    void reason;
    this.emit();
  }

  dispose(): void {
    this.clearTimers();
  }

  /** Test helper: advance inactivity check using injected now(). */
  checkInactivity(): void {
    const settings = this.deps.getSettings();
    const thresholdMs = settings.inactivityThresholdMinutes * 60_000;
    if (this.state === 'Tracking' && this.now() - this.lastActivityAt >= thresholdMs) {
      this.pause('inactivity');
    }
  }

  private armTimers(settings: ExtensionSettings): void {
    this.clearTimers();

    this.inactivityTimer = this.setIntervalFn(() => {
      this.checkInactivity();
    }, 30_000);

    if (settings.enableHeartbeat) {
      const intervalMs = Math.max(1, settings.heartbeatIntervalMinutes) * 60_000;
      this.heartbeatTimer = this.setIntervalFn(() => {
        void this.sendHeartbeat();
      }, intervalMs);
    }
  }

  private async sendHeartbeat(): Promise<void> {
    if (this.state !== 'Tracking') {
      return;
    }
    const repo = await this.deps.git.resolve();
    try {
      await this.deps.api.heartbeat({
        sessionId: this.sessionId,
        externalSessionId: this.externalSessionId,
        editor: 'VisualStudioCode',
        workspacePath: repo.workspacePath,
        repositoryPath: repo.repositoryPath,
        branch: repo.branch,
        timestampUtc: new Date(this.now()).toISOString(),
      });
      this.serverOnline = true;
    } catch {
      this.serverOnline = false;
      this.emit();
    }
  }

  private clearTimers(): void {
    if (this.inactivityTimer) {
      this.clearIntervalFn(this.inactivityTimer);
      this.inactivityTimer = undefined;
    }
    if (this.heartbeatTimer) {
      this.clearIntervalFn(this.heartbeatTimer);
      this.heartbeatTimer = undefined;
    }
  }

  private emit(): void {
    let state = this.state;
    if (!this.serverOnline) {
      state = 'Server Offline';
    } else if (this.state === 'Tracking' && this.projectName === 'No project') {
      state = 'Unallocated';
    }
    this.state = state;
    this.deps.onStateChange?.(state, this.projectName);
  }
}

function basename(p: string): string {
  const parts = p.replace(/\\/g, '/').split('/');
  return parts[parts.length - 1] || p;
}
