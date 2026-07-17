/** Shared types for MCP Track Tokens VS Code extension. */

export type ActivityEventType =
  | 'PromptSubmitted'
  | 'AgentStarted'
  | 'AgentCompleted'
  | 'AgentCancelled'
  | 'AgentFailed'
  | 'SessionStarted'
  | 'SessionEnded'
  | 'WorkspaceChanged'
  | 'Heartbeat';

export type TrackingState = 'Tracking' | 'Paused' | 'Unallocated' | 'Server Offline';

export type LogLevel = 'error' | 'warn' | 'info' | 'debug';

export interface ExtensionSettings {
  serverUrl: string;
  autoStartSession: boolean;
  inactivityThresholdMinutes: number;
  enableHeartbeat: boolean;
  heartbeatIntervalMinutes: number;
  enablePromptHashing: boolean;
  storePromptContent: boolean;
  showStatusBar: boolean;
  defaultProject: string;
  logLevel: LogLevel;
}

export interface RepoInfo {
  workspacePath?: string;
  repositoryPath?: string;
  remoteUrl?: string;
  branch?: string;
  activeFilePath?: string;
}

export interface IngestEvent {
  schemaVersion?: string;
  externalEventId?: string;
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
  responseContent?: string;
  status?: string;
  durationMilliseconds?: number;
  responseCompletedAtUtc?: string;
  metadata?: Record<string, unknown>;
}

export interface IngestEventResult {
  eventId: string;
  wasDuplicate: boolean;
  projectId?: string | null;
  sessionId?: string | null;
}

export interface SessionStartRequest {
  projectId?: string;
  editor: string;
  editorVersion?: string;
  machineName?: string;
  userName?: string;
  workspacePath?: string;
  repositoryPath?: string;
  remoteUrl?: string;
  branch?: string;
  externalSessionId?: string;
  notes?: string;
  startedAtUtc?: string;
}

export interface SessionEndRequest {
  sessionId?: string;
  externalSessionId?: string;
  editor?: string;
  notes?: string;
  endedAtUtc?: string;
}

export interface HeartbeatRequest {
  sessionId?: string;
  externalSessionId?: string;
  editor?: string;
  workspacePath?: string;
  repositoryPath?: string;
  branch?: string;
  timestampUtc?: string;
}

export interface TrackingStatus {
  isHealthy: boolean;
  currentProject?: {
    id: string;
    name: string;
  } | null;
  activeSessionId?: string | null;
  lastEventAtUtc?: string | null;
  queuedEventCount?: number;
  unallocatedEventCount?: number;
}

export interface CreateProjectRequest {
  name: string;
  slug?: string;
  clientName?: string;
  billingCode?: string;
  currency?: string;
  repositoryPath?: string;
  remoteUrl?: string;
  aliases?: string[];
}

export interface ProjectDetail {
  id: string;
  name: string;
  slug: string;
  primaryRepositoryPath?: string | null;
  primaryRemoteUrl?: string | null;
}

export interface PrivacyOptions {
  enablePromptHashing: boolean;
  storePromptContent: boolean;
  hashSalt?: string;
}

export interface PrivacyResult {
  promptLength: number;
  promptHash?: string;
  promptContent?: string;
}

export interface StatusBarModel {
  state: TrackingState;
  projectName: string;
}
