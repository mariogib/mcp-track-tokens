/** API types aligned with McpTrackTokens.Application DTOs (camelCase JSON). */

export interface ProjectDto {
  id: string;
  name: string;
  slug: string;
  clientName?: string | null;
  billingCode?: string | null;
  currency: string;
  primaryRepositoryPath?: string | null;
  primaryRemoteUrl?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  repositoryCount: number;
  lastActivityAtUtc?: string | null;
  promptCount?: number;
  agentDurationMilliseconds?: number;
  activeProjectTimeSeconds?: number;
  usageBasedCost?: number;
  subscriptionAllocation?: number;
  totalAiCost?: number;
}

export interface UpdateProjectRequest {
  name?: string | null;
  slug?: string | null;
  clientName?: string | null;
  billingCode?: string | null;
  currency?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  isActive?: boolean | null;
}

export interface ProjectRepositoryDto {
  id: string;
  projectId: string;
  localPath: string;
  normalizedPath: string;
  remoteUrl?: string | null;
  normalizedRemoteUrl?: string | null;
  defaultBranch?: string | null;
  isActive: boolean;
}

export interface ProjectAliasDto {
  id: string;
  projectId: string;
  alias: string;
  normalizedAlias: string;
  aliasType: string;
}

export interface ActivitySummaryDto {
  promptCount: number;
  agentRuns: number;
  agentDurationMilliseconds: number;
  activeProjectTimeSeconds: number;
  sessionCount: number;
  failureCount: number;
  cancellationCount: number;
  fromUtc?: string | null;
  toUtc?: string | null;
}

export interface UsageSummaryDto {
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  requestCount: number;
  reportedCost: number;
  currency: string;
  fromUtc?: string | null;
  toUtc?: string | null;
}

export interface CostSummaryDto {
  usageBasedCost: number;
  subscriptionAllocation: number;
  otherProviderCost: number;
  unallocatedCost: number;
  totalAiCost: number;
  currency: string;
  fromUtc?: string | null;
  toUtc?: string | null;
}

export interface ProjectDetailDto {
  id: string;
  name: string;
  slug: string;
  clientName?: string | null;
  billingCode?: string | null;
  currency: string;
  primaryRepositoryPath?: string | null;
  primaryRemoteUrl?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  repositories: ProjectRepositoryDto[];
  aliases: ProjectAliasDto[];
  activity?: ActivitySummaryDto | null;
  usage?: UsageSummaryDto | null;
  cost?: CostSummaryDto | null;
}

export interface DailyActivityRow {
  day: string;
  projectId?: string | null;
  projectName?: string | null;
  editor?: string | null;
  promptCount: number;
  agentRuns: number;
  agentDurationMilliseconds: number;
  activeProjectTimeSeconds: number;
  sessionCount: number;
  totalTokens?: number;
}

export interface NamedMetricRow {
  name: string;
  promptCount: number;
  agentRuns: number;
  agentDurationMilliseconds: number;
  activeProjectTimeSeconds: number;
  usageBasedCost: number;
  subscriptionAllocation: number;
}

export interface ProjectActivityReport {
  projectId: string;
  projectName: string;
  projectSlug: string;
  fromUtc: string;
  toUtc: string;
  promptCount: number;
  agentRuns: number;
  agentDurationMilliseconds: number;
  activeProjectTimeSeconds: number;
  sessionCount: number;
  failureCount: number;
  cancellationCount: number;
  byDay: DailyActivityRow[];
  byEditor: NamedMetricRow[];
  byBranch: NamedMetricRow[];
}

export interface ProjectCostReport {
  projectId: string;
  projectName: string;
  clientName?: string | null;
  fromUtc: string;
  toUtc: string;
  currency: string;
  activeProjectTimeSeconds: number;
  agentDurationMilliseconds: number;
  promptCount: number;
  importedTotalTokens: number;
  usageBasedCursorCost: number;
  subscriptionAllocation: number;
  otherProviderCost: number;
  unallocatedCost: number;
  totalAiCost: number;
  byModel: NamedMetricRow[];
}

export interface TokenCostModelRow {
  model: string;
  rateSource: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  estimatedCost: number;
  reportedCost: number;
  inputPerMillion: number;
  outputPerMillion: number;
  cacheReadPerMillion: number;
  reasoningPerMillion?: number | null;
}

export interface ProjectTokenCostEstimate {
  projectId: string;
  projectName: string;
  fromUtc: string;
  toUtc: string;
  currency: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  estimatedCost: number;
  reportedCost: number;
  rateCardModelCount: number;
  hasRateCard: boolean;
  byModel: TokenCostModelRow[];
}

export interface TrackingStatusDto {
  isHealthy: boolean;
  databasePath: string;
  databaseProvider?: string | null;
  currentProject?: ProjectDto | null;
  activeSessionId?: string | null;
  activeSessionEditor?: string | null;
  lastEventAtUtc?: string | null;
  lastEventType?: string | null;
  queuedEventCount: number;
  unallocatedEventCount: number;
  unallocatedUsageCount: number;
  lastCursorImportAtUtc?: string | null;
  lastCursorImportStatus?: string | null;
}

export interface UnallocatedItemDto {
  id: string;
  kind: string;
  timestampUtc: string;
  editor?: string | null;
  model?: string | null;
  provider?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  externalSessionId?: string | null;
  externalRequestId?: string | null;
  totalTokens?: number | null;
  reportedCost?: number | null;
  currency?: string | null;
  suggestedProjectName?: string | null;
  suggestedProjectId?: string | null;
  suggestedMethod?: string | null;
  suggestedConfidence?: string | null;
  reason?: string | null;
  workspacePath?: string | null;
  eventType?: string | null;
  durationMilliseconds?: number | null;
}

export interface UnallocatedUsageReport {
  fromUtc: string;
  toUtc: string;
  count: number;
  totalCost: number;
  currency: string;
  items: UnallocatedItemDto[];
}

export interface ImportedUsageItemDto {
  id: string;
  timestampUtc: string;
  source: string;
  externalRecordId?: string | null;
  model?: string | null;
  provider?: string | null;
  inputTokens?: number | null;
  outputTokens?: number | null;
  cachedInputTokens?: number | null;
  totalTokens: number;
  reportedCost: number;
  currency: string;
  requestCount?: number | null;
  importBatchId?: string | null;
  importedAtUtc: string;
  projectId?: string | null;
  projectName?: string | null;
  activityEventId?: string | null;
  attributionMethod?: string | null;
}

export interface ImportedUsageReport {
  fromUtc: string;
  toUtc: string;
  count: number;
  totalTokens: number;
  totalCost: number;
  currency: string;
  items: ImportedUsageItemDto[];
}

export interface UnallocatedBundle {
  activity: UnallocatedItemDto[];
  usage: UnallocatedUsageReport;
}

export interface AssignActivityRequestDto {
  projectId: string;
  eventIds: string[];
}

export interface AssignActivityResultDto {
  projectId: string;
  assigned: number;
}

export interface MonthlySummaryReport {
  year: number;
  month: number;
  fromUtc: string;
  toUtc: string;
  currency: string;
  activity: ActivitySummaryDto;
  usage: UsageSummaryDto;
  cost: CostSummaryDto;
  projects: ProjectCostReport[];
}

export interface ActiveSessionDto {
  id: string;
  projectId?: string | null;
  projectName?: string | null;
  editor?: string | null;
  startedAtUtc: string;
  lastHeartbeatAtUtc?: string | null;
  branch?: string | null;
  repositoryPath?: string | null;
}

export interface ImportPreviewDto {
  fileName: string;
  fileHash?: string | null;
  detectedFormat: string;
  source: string;
  columns: string[];
  columnMappings: Record<string, string>;
  receivedCount: number;
  validCount: number;
  duplicateCount: number;
  invalidCount: number;
  warnings: string[];
  sampleRecords: NormalizedUsageRecordDto[];
}

export interface NormalizedUsageRecordDto {
  externalRecordId?: string | null;
  timestampUtc: string;
  model?: string | null;
  provider?: string | null;
  inputTokens?: number | null;
  outputTokens?: number | null;
  totalTokens?: number | null;
  reportedCost?: number | null;
  currency?: string | null;
}

export interface ImportResultDto {
  importBatchId?: string | null;
  dryRun: boolean;
  fileName: string;
  fileHash?: string | null;
  source: string;
  status: string;
  receivedCount: number;
  importedCount: number;
  duplicateCount: number;
  failedCount: number;
  errorSummary?: string | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
}

export interface ReconciliationRequestDto {
  fromUtc: string;
  toUtc: string;
  dryRun: boolean;
  includeLowConfidence: boolean;
}

export interface UsageAttributionRow {
  usageRecordId: string;
  attributionId?: string | null;
  projectId?: string | null;
  projectName?: string | null;
  activityEventId?: string | null;
  timestampUtc: string;
  model?: string | null;
  provider?: string | null;
  allocatedCost: number;
  allocationPercentage: number;
  allocatedTotalTokens: number;
  attributionMethod: string;
  confidence: string;
  reason?: string | null;
}

export interface ReconciliationResultDto {
  dryRun: boolean;
  fromUtc: string;
  toUtc: string;
  processedCount: number;
  allocatedCount: number;
  unallocatedCount: number;
  skippedCount: number;
  attributions: UsageAttributionRow[];
  /** Rows that could not be linked to a prior prompt. */
  unallocated: UsageAttributionRow[];
}

export interface AllocationRequestDto {
  usageRecordId: string;
  projectAllocations: ProjectAllocationShareDto[];
  reason?: string | null;
  reviewedBy?: string | null;
  replaceExisting?: boolean;
}

export interface ProjectAllocationShareDto {
  projectId: string;
  percentage: number;
  editorSessionId?: string | null;
  activityEventId?: string | null;
}

export interface CreateApiKeyRequestDto {
  name: string;
  expiresAtUtc?: string | null;
  allowedEditors?: string | null;
  allowedMachineNames?: string | null;
}

export interface ApiKeyCreateResultDto {
  id: string;
  name: string;
  apiKey: string;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  allowedEditors?: string | null;
  allowedMachineNames?: string | null;
}

export interface ApiKeyDto {
  id: string;
  name: string;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  lastUsedAtUtc?: string | null;
  isActive: boolean;
  allowedEditors?: string | null;
  allowedMachineNames?: string | null;
}

export interface SettingsDto {
  inactivityThresholdMinutes: number;
  defaultCurrency: string;
  cursorSubscriptionAmount: number;
  cursorSubscriptionCurrency: string;
  cursorAllocationMethod: string;
  storePromptContent: boolean;
  storeResponseContent: boolean;
  enablePromptHashing: boolean;
  exportPath: string;
  databasePath: string;
  databaseProvider: string;
  dataRetentionDays?: number | null;
  serverUrl: string;
  autoCreateProjects: boolean;
  estimateCostFromTokenRates?: boolean;
  cursorTokenRates?: CursorModelTokenRateDto[];
}

export interface CursorModelTokenRateDto {
  model: string;
  inputPerMillion: number;
  outputPerMillion: number;
  cacheReadPerMillion: number;
  cacheWritePerMillion: number;
  reasoningPerMillion?: number | null;
}

export interface UpdateSettingsRequest {
  inactivityThresholdMinutes?: number;
  defaultCurrency?: string;
  cursorSubscriptionAmount?: number;
  cursorSubscriptionCurrency?: string;
  cursorAllocationMethod?: string;
  storePromptContent?: boolean;
  storeResponseContent?: boolean;
  enablePromptHashing?: boolean;
  exportPath?: string;
  dataRetentionDays?: number | null;
  autoCreateProjects?: boolean;
  estimateCostFromTokenRates?: boolean;
  cursorTokenRates?: CursorModelTokenRateDto[];
}

export interface ExportRequestDto {
  reportType: string;
  format: string;
  projectId?: string | null;
  fromUtc: string;
  toUtc: string;
  outputDirectory?: string | null;
  fileName?: string | null;
  includeActivity?: boolean;
  includeUsage?: boolean;
  includeCosts?: boolean;
}

export interface ExportResultDto {
  filePath: string;
  format: string;
  byteCount: number;
  exportedAtUtc: string;
}

export interface HealthDto {
  status: string;
  healthy?: boolean;
}

export interface PromptEventDto {
  id: string;
  timestampUtc: string;
  eventType: string;
  editor?: string | null;
  model?: string | null;
  branch?: string | null;
  status?: string | null;
  durationMilliseconds?: number | null;
  repositoryPath?: string | null;
  /** Present when reconciliation linked this prompt to imported usage. */
  totalTokens?: number | null;
  reportedCost?: number | null;
  /** Number of imported usage rows linked to this prompt (many-to-one). */
  linkedUsageCount?: number;
  hasLinkedUsage?: boolean;
}

export interface SessionDto {
  id: string;
  projectId?: string | null;
  editor?: string | null;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  lastHeartbeatAtUtc?: string | null;
  branch?: string | null;
  isActive: boolean;
}

export interface DateRangeParams {
  fromUtc: string;
  toUtc: string;
}

export interface IntegrationStatusDto {
  cursorHooksConfigured: boolean;
  vscodeExtensionDetected: boolean;
  mcpConfigured: boolean;
  lastIngestAtUtc?: string | null;
  notes?: string[];
}
