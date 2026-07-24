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

export interface CreateProjectRequest {
  name: string;
  slug?: string | null;
  clientName?: string | null;
  billingCode?: string | null;
  currency?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  aliases?: string[] | null;
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
  calculatedTokenCost?: number;
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
  calculatedTokenCost?: number;
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
  calculatedTokenCost?: number;
  hasRateCard?: boolean;
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
  calculatedTokenCost?: number;
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
  totalCalculatedTokenCost?: number;
  currency: string;
  items: UnallocatedItemDto[];
}

export interface DeleteUnallocatedUsageResultDto {
  fromUtc: string;
  toUtc: string;
  deletedCount: number;
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
  calculatedTokenCost?: number;
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
  totalCalculatedTokenCost?: number;
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

export interface DeleteActivityRequestDto {
  eventIds: string[];
}

export interface DeleteActivityResultDto {
  deleted: number;
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

export interface ReportClientDto {
  name: string;
  projectCount: number;
  currency: string;
}

export interface ClientCostReport {
  clientName: string;
  fromUtc: string;
  toUtc: string;
  currency: string;
  projectCount: number;
  activeProjectTimeSeconds: number;
  agentDurationMilliseconds: number;
  promptCount: number;
  usageBasedCost: number;
  subscriptionAllocation: number;
  otherProviderCost: number;
  totalAiCost: number;
  calculatedTokenCost?: number;
  hasRateCard?: boolean;
  projects: ProjectCostReport[];
}

export interface ClientTokenCostEstimate {
  clientName: string;
  fromUtc: string;
  toUtc: string;
  currency: string;
  projectCount: number;
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
  projects: ProjectTokenCostEstimate[];
}

export interface EditorComparisonReport {
  fromUtc: string;
  toUtc: string;
  editors: NamedMetricRow[];
}

export interface ModelCostRow {
  model: string;
  provider?: string | null;
  totalTokens: number;
  requestCount: number;
  usageBasedCost: number;
  allocatedCost: number;
  unallocatedCost: number;
  calculatedTokenCost?: number;
}

export interface ModelCostReport {
  fromUtc: string;
  toUtc: string;
  currency: string;
  calculatedTokenCost?: number;
  hasRateCard?: boolean;
  models: ModelCostRow[];
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
  calculatedTokenCost?: number;
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
  sessionInactivityCloseMinutes?: number;
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

export interface CursorDocsPricingFetchResultDto {
  sourceUrl: string;
  fetchedAtUtc: string;
  count: number;
  saved?: boolean;
  warnings: string[];
  rates: CursorModelTokenRateDto[];
}

export interface UpdateSettingsRequest {
  inactivityThresholdMinutes?: number;
  sessionInactivityCloseMinutes?: number;
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
  /** Rate-card calculated cost for linked usage (Settings Cursor token rates). */
  calculatedTokenCost?: number | null;
  /** Number of imported usage rows linked to this prompt (many-to-one). */
  linkedUsageCount?: number;
  hasLinkedUsage?: boolean;
}

export interface PagedResult<T> {
  items: T[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
}

export interface PromptFacetsDto {
  models: string[];
  branches: string[];
  eventTypes: string[];
  days: string[];
}

export interface PromptBrowseQuery {
  fromUtc: string;
  toUtc: string;
  pageIndex: number;
  pageSize: number;
  search?: string;
  status?: string;
  eventType?: string;
  model?: string;
  branch?: string;
}

export interface TimesheetBrowseQuery {
  projectId?: string;
  fromUtc?: string;
  toUtc?: string;
  pageIndex: number;
  pageSize: number;
  search?: string;
  openClosed?: string;
}

export interface SessionDto {
  id: string;
  projectId?: string | null;
  editor?: string | null;
  editorVersion?: string | null;
  machineName?: string | null;
  userName?: string | null;
  workspacePath?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  lastActivityAtUtc?: string | null;
  lastHeartbeatAtUtc?: string | null;
  branch?: string | null;
  externalSessionId?: string | null;
  status?: string | null;
  isActive: boolean;
}

export interface CreateProjectSessionRequest {
  editor: string;
  editorVersion?: string | null;
  machineName?: string | null;
  userName?: string | null;
  workspacePath?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  branch?: string | null;
  externalSessionId?: string | null;
  startedAtUtc?: string | null;
  endedAtUtc?: string | null;
  status?: string | null;
}

export interface UpdateSessionRequest {
  projectId?: string | null;
  editor: string;
  editorVersion?: string | null;
  machineName?: string | null;
  userName?: string | null;
  workspacePath?: string | null;
  repositoryPath?: string | null;
  remoteUrl?: string | null;
  branch?: string | null;
  externalSessionId?: string | null;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  status: string;
}

export interface TimesheetCategoryDto {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateTimesheetCategoryRequest {
  name: string;
  sortOrder?: number | null;
}

export interface UpdateTimesheetCategoryRequest {
  name: string;
  sortOrder: number;
  isActive: boolean;
}

export interface TimesheetEntryDto {
  id: string;
  projectId: string;
  projectName?: string | null;
  categoryId: string;
  categoryName: string;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  notes?: string | null;
  isOpen: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface StartTimesheetRequest {
  projectId?: string | null;
  categoryId?: string | null;
  category?: string | null;
  startedAtUtc?: string | null;
  notes?: string | null;
}

export interface EndTimesheetRequest {
  projectId?: string | null;
  timesheetEntryId?: string | null;
  endedAtUtc?: string | null;
  appendNotes?: string | null;
}

export interface CreateTimesheetEntryRequest {
  categoryId?: string | null;
  category?: string | null;
  startedAtUtc?: string | null;
  endedAtUtc?: string | null;
  notes?: string | null;
}

export interface UpdateTimesheetEntryRequest {
  categoryId: string;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  notes?: string | null;
}

export interface TimesheetReportTotals {
  totalDurationSeconds: number;
  entryCount: number;
  openEntryCount: number;
}

export interface TimesheetCategoryBreakdownRow {
  categoryId: string;
  categoryName: string;
  durationSeconds: number;
  entryCount: number;
}

export interface TimesheetProjectBreakdownRow {
  projectId: string;
  projectName: string;
  clientName?: string | null;
  durationSeconds: number;
  entryCount: number;
}

export interface TimesheetClientBreakdownRow {
  clientName: string;
  durationSeconds: number;
  entryCount: number;
  projectCount: number;
}

export interface TimesheetDailyBreakdownRow {
  day: string;
  durationSeconds: number;
  entryCount: number;
}

export interface TimesheetOverallReport {
  fromUtc: string;
  toUtc: string;
  totals: TimesheetReportTotals;
  byCategory: TimesheetCategoryBreakdownRow[];
  byProject: TimesheetProjectBreakdownRow[];
  byClient: TimesheetClientBreakdownRow[];
  byDay: TimesheetDailyBreakdownRow[];
}

export interface TimesheetProjectReport {
  projectId: string;
  projectName: string;
  clientName?: string | null;
  fromUtc: string;
  toUtc: string;
  totals: TimesheetReportTotals;
  byCategory: TimesheetCategoryBreakdownRow[];
  byDay: TimesheetDailyBreakdownRow[];
}

export interface TimesheetClientReport {
  clientName: string;
  fromUtc: string;
  toUtc: string;
  totals: TimesheetReportTotals;
  byProject: TimesheetProjectBreakdownRow[];
  byCategory: TimesheetCategoryBreakdownRow[];
  byDay: TimesheetDailyBreakdownRow[];
}

export interface DateRangeParams {
  fromUtc: string;
  toUtc: string;
}

export interface IntegrationStatusDto {
  cursorHooksConfigured: boolean;
  cursorHooksOnDisk?: boolean;
  cursorHooksInferredFromActivity?: boolean;
  mcpConfigured: boolean;
  lastIngestAtUtc?: string | null;
  notes?: string[];
}

export interface CursorHooksCompatibilityCheckDto {
  id: string;
  status: string;
  message: string;
}

export interface CursorHooksCompatibilityReportDto {
  status: string;
  summary: string;
  cursorVersion?: string | null;
  cursorVersionSource?: string | null;
  cursorUserDirectory: string;
  hooksInstallDirectory?: string | null;
  hooksConfigPath?: string | null;
  hooksConfigSchemaVersion?: number | null;
  checks: CursorHooksCompatibilityCheckDto[];
  wiredEvents: string[];
  legacyEvents: string[];
  recommendations: string[];
  lastCursorEventAtUtc?: string | null;
  lastCursorEventEditorVersion?: string | null;
  probeEventId?: string | null;
  probeIngestedAtUtc?: string | null;
}

export interface OfflineQueueReplayResultDto {
  attempted: number;
  flushed: number;
  remaining: number;
  failed: number;
  errors: string[];
}

export interface DatabaseBackupFileDto {
  fileName: string;
  fullPath: string;
  sizeBytes: number;
  createdAtUtc: string;
}

export interface DatabaseBackupInfoDto {
  databasePath: string;
  databaseProvider: string;
  supportsBackup: boolean;
  defaultFolder: string;
  destinationFolder: string;
  backups: DatabaseBackupFileDto[];
}

export interface DatabaseBackupResultDto {
  filePath: string;
  sizeBytes: number;
  createdAtUtc: string;
  message: string;
}

export interface DatabaseRestoreResultDto {
  restoredFromPath: string;
  safetyBackupPath?: string | null;
  restartRecommended: boolean;
  message: string;
}
