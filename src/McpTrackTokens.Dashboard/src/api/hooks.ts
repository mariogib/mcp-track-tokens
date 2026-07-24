import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  AllocationRequestDto,
  AssignActivityRequestDto,
  DeleteActivityRequestDto,
  CreateApiKeyRequestDto,
  CreateProjectRequest,
  CreateProjectSessionRequest,
  CreateTimesheetCategoryRequest,
  CreateTimesheetEntryRequest,
  EndTimesheetRequest,
  ExportRequestDto,
  PromptBrowseQuery,
  ReconciliationRequestDto,
  StartTimesheetRequest,
  TimesheetBrowseQuery,
  UpdateSettingsRequest,
  UpdateProjectRequest,
  UpdateSessionRequest,
  UpdateTimesheetCategoryRequest,
  UpdateTimesheetEntryRequest,
} from './types';

export const queryKeys = {
  health: ['health'] as const,
  ready: ['ready'] as const,
  status: ['status'] as const,
  summary: (year: number, month: number) => ['reports', 'summary', year, month] as const,
  reportClients: ['reports', 'clients'] as const,
  clientCost: (clientName: string, from: string, to: string) =>
    ['reports', 'client-cost', clientName, from, to] as const,
  clientTokenCost: (clientName: string, from: string, to: string) =>
    ['reports', 'client-token-cost', clientName, from, to] as const,
  modelCost: (from: string, to: string) => ['reports', 'model-cost', from, to] as const,
  editorComparison: (from: string, to: string) => ['reports', 'editors', from, to] as const,
  projects: ['projects'] as const,
  project: (id: string) => ['projects', id] as const,
  projectActivity: (id: string, from: string, to: string) =>
    ['projects', id, 'activity', from, to] as const,
  projectUsage: (id: string, from: string, to: string) =>
    ['projects', id, 'usage', from, to] as const,
  projectCost: (id: string, from: string, to: string) =>
    ['projects', id, 'cost', from, to] as const,
  projectTokenCost: (id: string, from: string, to: string) =>
    ['projects', id, 'token-cost', from, to] as const,
  projectPrompts: (id: string, from: string, to: string) =>
    ['projects', id, 'prompts', from, to] as const,
  projectPromptsPaged: (
    id: string,
    from: string,
    to: string,
    pageIndex: number,
    pageSize: number,
    search: string,
    status: string,
    eventType: string,
    model: string,
    branch: string,
  ) =>
    [
      'projects',
      id,
      'prompts',
      'paged',
      from,
      to,
      pageIndex,
      pageSize,
      search,
      status,
      eventType,
      model,
      branch,
    ] as const,
  projectPromptFacets: (id: string, from: string, to: string) =>
    ['projects', id, 'prompts', 'facets', from, to] as const,
  projectSessions: (id: string, from?: string, to?: string) =>
    ['projects', id, 'sessions', from, to] as const,
  sessions: (projectId?: string, from?: string, to?: string) =>
    ['sessions', projectId ?? 'all', from, to] as const,
  sessionPrompts: (id: string) => ['sessions', id, 'prompts'] as const,
  projectTimesheet: (id: string, from?: string, to?: string) =>
    ['projects', id, 'timesheet', from, to] as const,
  projectTimesheetPaged: (
    id: string,
    from: string | undefined,
    to: string | undefined,
    pageIndex: number,
    pageSize: number,
    search: string,
    openClosed: string,
  ) =>
    [
      'projects',
      id,
      'timesheet',
      'paged',
      from,
      to,
      pageIndex,
      pageSize,
      search,
      openClosed,
    ] as const,
  timesheetEntries: (projectId?: string, from?: string, to?: string) =>
    ['timesheet-entries', projectId ?? 'all', from, to] as const,
  timesheetEntriesPaged: (
    projectId: string | undefined,
    from: string | undefined,
    to: string | undefined,
    pageIndex: number,
    pageSize: number,
    search: string,
    openClosed: string,
  ) =>
    [
      'timesheet-entries',
      'paged',
      projectId ?? 'all',
      from,
      to,
      pageIndex,
      pageSize,
      search,
      openClosed,
    ] as const,
  timesheetOverallReport: (from: string, to: string) =>
    ['timesheet-reports', 'overall', from, to] as const,
  timesheetProjectReport: (projectId: string, from: string, to: string) =>
    ['timesheet-reports', 'project', projectId, from, to] as const,
  timesheetClientReport: (clientName: string, from: string, to: string) =>
    ['timesheet-reports', 'client', clientName, from, to] as const,
  activeSession: ['sessions', 'active'] as const,
  unallocated: (from?: string, to?: string) => ['unallocated', from, to] as const,
  importedUsage: (from?: string, to?: string) => ['imported-usage', from, to] as const,
  settings: ['settings'] as const,
  apiKeys: ['api-keys'] as const,
  timesheetCategories: (activeOnly?: boolean) =>
    ['timesheet-categories', activeOnly ?? 'all'] as const,
  integrations: ['integrations'] as const,
  databaseBackupInfo: (destinationDirectory?: string) =>
    ['database-backup-info', destinationDirectory ?? ''] as const,
};

export function useHealthQuery() {
  return useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => api.health(signal),
    refetchInterval: 30_000,
    retry: 1,
  });
}

export function useStatusQuery() {
  return useQuery({
    queryKey: queryKeys.status,
    queryFn: ({ signal }) => api.status(signal),
    refetchInterval: 15_000,
  });
}

export function useReportsSummaryQuery(year: number, month: number) {
  return useQuery({
    queryKey: queryKeys.summary(year, month),
    queryFn: ({ signal }) => api.reportsSummary(year, month, signal),
  });
}

export function useReportClientsQuery() {
  return useQuery({
    queryKey: queryKeys.reportClients,
    queryFn: ({ signal }) => api.listReportClients(signal),
  });
}

export function useClientCostQuery(
  clientName: string | undefined,
  fromUtc: string,
  toUtc: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.clientCost(clientName ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getClientCost(clientName!, fromUtc, toUtc, signal),
    enabled: Boolean(clientName) && enabled,
  });
}

export function useClientTokenCostQuery(
  clientName: string | undefined,
  fromUtc: string,
  toUtc: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.clientTokenCost(clientName ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getClientTokenCost(clientName!, fromUtc, toUtc, signal),
    enabled: Boolean(clientName) && enabled,
  });
}

export function useModelCostReportQuery(fromUtc: string, toUtc: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.modelCost(fromUtc, toUtc),
    queryFn: ({ signal }) => api.getModelCostReport(fromUtc, toUtc, signal),
    enabled,
  });
}

export function useEditorComparisonReportQuery(fromUtc: string, toUtc: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.editorComparison(fromUtc, toUtc),
    queryFn: ({ signal }) => api.getEditorComparisonReport(fromUtc, toUtc, signal),
    enabled,
  });
}

export function useProjectsQuery() {
  return useQuery({
    queryKey: queryKeys.projects,
    queryFn: ({ signal }) => api.listProjects(signal),
  });
}

export function useUpdateProjectMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateProjectRequest }) =>
      api.updateProject(id, body),
    onSuccess: (_data, variables) => {
      void qc.invalidateQueries({ queryKey: queryKeys.projects });
      void qc.invalidateQueries({ queryKey: queryKeys.project(variables.id) });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useCreateProjectMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateProjectRequest) => api.createProject(body),
    onSuccess: (data) => {
      void qc.invalidateQueries({ queryKey: queryKeys.projects });
      void qc.invalidateQueries({ queryKey: queryKeys.project(data.id) });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useDeleteProjectMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.deleteProject(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: queryKeys.projects });
      void qc.invalidateQueries({ queryKey: queryKeys.project(id) });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useProjectQuery(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.project(id ?? ''),
    queryFn: ({ signal }) => api.getProject(id!, signal),
    enabled: Boolean(id),
  });
}

export function useProjectActivityQuery(id: string | undefined, fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.projectActivity(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectActivity(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectUsageQuery(id: string | undefined, fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.projectUsage(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectUsage(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectCostQuery(id: string | undefined, fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.projectCost(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectCost(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectTokenCostQuery(id: string | undefined, fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.projectTokenCost(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectTokenCost(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectPromptsQuery(id: string | undefined, fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.projectPrompts(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectPrompts(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectPromptFacetsQuery(
  id: string | undefined,
  fromUtc: string,
  toUtc: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.projectPromptFacets(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectPromptFacets(id!, fromUtc, toUtc, signal),
    enabled: enabled && Boolean(id),
  });
}

export function useProjectPromptsPagedQuery(
  id: string | undefined,
  query: PromptBrowseQuery | null,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.projectPromptsPaged(
      id ?? '',
      query?.fromUtc ?? '',
      query?.toUtc ?? '',
      query?.pageIndex ?? 0,
      query?.pageSize ?? 25,
      query?.search ?? '',
      query?.status ?? '',
      query?.eventType ?? '',
      query?.model ?? '',
      query?.branch ?? '',
    ),
    queryFn: ({ signal }) => api.getProjectPromptsPaged(id!, query!, signal),
    enabled: enabled && Boolean(id) && Boolean(query),
  });
}

export function useProjectSessionsQuery(id: string | undefined, fromUtc?: string, toUtc?: string) {
  return useQuery({
    queryKey: queryKeys.projectSessions(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectSessions(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useSessionsQuery(
  params?: { projectId?: string; fromUtc?: string; toUtc?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.sessions(params?.projectId, params?.fromUtc, params?.toUtc),
    queryFn: ({ signal }) => api.getSessions(params, signal),
    enabled,
  });
}

export function useSessionPromptsQuery(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.sessionPrompts(id ?? ''),
    queryFn: ({ signal }) => api.getSessionPrompts(id!, signal),
    enabled: enabled && Boolean(id),
  });
}

export function useCreateProjectSessionMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      projectId,
      body,
    }: {
      projectId: string;
      body: CreateProjectSessionRequest;
    }) => api.createProjectSession(projectId, body),
    onSuccess: (_data, variables) => {
      void qc.invalidateQueries({ queryKey: ['projects', variables.projectId, 'sessions'] });
      void qc.invalidateQueries({ queryKey: queryKeys.activeSession });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useUpdateSessionMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSessionRequest }) =>
      api.updateSession(id, body),
    onSuccess: (data) => {
      if (data.projectId) {
        void qc.invalidateQueries({ queryKey: ['projects', data.projectId, 'sessions'] });
      } else {
        void qc.invalidateQueries({ queryKey: ['projects'] });
      }
      void qc.invalidateQueries({ queryKey: queryKeys.activeSession });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useDeleteSessionMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id }: { id: string; projectId?: string | null }) => api.deleteSession(id),
    onSuccess: (_data, variables) => {
      if (variables.projectId) {
        void qc.invalidateQueries({ queryKey: ['projects', variables.projectId, 'sessions'] });
      } else {
        void qc.invalidateQueries({ queryKey: ['projects'] });
      }
      void qc.invalidateQueries({ queryKey: queryKeys.activeSession });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useProjectTimesheetQuery(id: string | undefined, fromUtc?: string, toUtc?: string) {
  return useQuery({
    queryKey: queryKeys.projectTimesheet(id ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getProjectTimesheetEntries(id!, fromUtc, toUtc, signal),
    enabled: Boolean(id),
  });
}

export function useProjectTimesheetPagedQuery(
  id: string | undefined,
  query: TimesheetBrowseQuery | null,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.projectTimesheetPaged(
      id ?? '',
      query?.fromUtc,
      query?.toUtc,
      query?.pageIndex ?? 0,
      query?.pageSize ?? 25,
      query?.search ?? '',
      query?.openClosed ?? '',
    ),
    queryFn: ({ signal }) => api.getProjectTimesheetEntriesPaged(id!, query!, signal),
    enabled: enabled && Boolean(id) && Boolean(query),
  });
}

export function useTimesheetEntriesQuery(
  params?: { projectId?: string; fromUtc?: string; toUtc?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.timesheetEntries(params?.projectId, params?.fromUtc, params?.toUtc),
    queryFn: ({ signal }) => api.getTimesheetEntries(params, signal),
    enabled,
  });
}

export function useTimesheetEntriesPagedQuery(
  query: TimesheetBrowseQuery | null,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.timesheetEntriesPaged(
      query?.projectId,
      query?.fromUtc,
      query?.toUtc,
      query?.pageIndex ?? 0,
      query?.pageSize ?? 25,
      query?.search ?? '',
      query?.openClosed ?? '',
    ),
    queryFn: ({ signal }) => api.getTimesheetEntriesPaged(query!, signal),
    enabled: enabled && Boolean(query),
  });
}

function invalidateTimesheetQueries(
  qc: ReturnType<typeof useQueryClient>,
  projectId?: string | null,
) {
  void qc.invalidateQueries({ queryKey: ['timesheet-entries'] });
  if (projectId) {
    void qc.invalidateQueries({ queryKey: ['projects', projectId, 'timesheet'] });
  } else {
    void qc.invalidateQueries({ queryKey: ['projects'] });
  }
}

export function useCreateTimesheetEntryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      projectId,
      body,
    }: {
      projectId: string;
      body: CreateTimesheetEntryRequest;
    }) => api.createProjectTimesheetEntry(projectId, body),
    onSuccess: (_data, variables) => {
      invalidateTimesheetQueries(qc, variables.projectId);
    },
  });
}

export function useStartTimesheetMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: StartTimesheetRequest) => api.startTimesheet(body),
    onSuccess: (data) => {
      invalidateTimesheetQueries(qc, data.projectId);
    },
  });
}

export function useEndTimesheetMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: EndTimesheetRequest) => api.endTimesheet(body),
    onSuccess: (data) => {
      invalidateTimesheetQueries(qc, data.projectId);
    },
  });
}

export function useUpdateTimesheetEntryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateTimesheetEntryRequest }) =>
      api.updateTimesheetEntry(id, body),
    onSuccess: (data) => {
      invalidateTimesheetQueries(qc, data.projectId);
    },
  });
}

export function useDeleteTimesheetEntryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id }: { id: string; projectId?: string | null }) =>
      api.deleteTimesheetEntry(id),
    onSuccess: (_data, variables) => {
      invalidateTimesheetQueries(qc, variables.projectId);
    },
  });
}

export function useTimesheetOverallReportQuery(fromUtc: string, toUtc: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.timesheetOverallReport(fromUtc, toUtc),
    queryFn: ({ signal }) => api.getTimesheetOverallReport(fromUtc, toUtc, signal),
    enabled,
  });
}

export function useTimesheetProjectReportQuery(
  projectId: string | undefined,
  fromUtc: string,
  toUtc: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.timesheetProjectReport(projectId ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getTimesheetProjectReport(projectId!, fromUtc, toUtc, signal),
    enabled: enabled && Boolean(projectId),
  });
}

export function useTimesheetClientReportQuery(
  clientName: string | undefined,
  fromUtc: string,
  toUtc: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.timesheetClientReport(clientName ?? '', fromUtc, toUtc),
    queryFn: ({ signal }) => api.getTimesheetClientReport(clientName!, fromUtc, toUtc, signal),
    enabled: enabled && Boolean(clientName),
  });
}

export function useActiveSessionQuery() {
  return useQuery({
    queryKey: queryKeys.activeSession,
    queryFn: ({ signal }) => api.activeSession(signal),
    refetchInterval: 10_000,
  });
}

export function useUnallocatedQuery(fromUtc?: string, toUtc?: string) {
  return useQuery({
    queryKey: queryKeys.unallocated(fromUtc, toUtc),
    queryFn: ({ signal }) => api.unallocated(fromUtc, toUtc, signal),
  });
}

export function useImportedUsageQuery(fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.importedUsage(fromUtc, toUtc),
    queryFn: ({ signal }) => api.importedUsage(fromUtc, toUtc, signal),
  });
}

export function useSettingsQuery() {
  return useQuery({
    queryKey: queryKeys.settings,
    queryFn: ({ signal }) => api.getSettings(signal),
  });
}

export function useApiKeysQuery() {
  return useQuery({
    queryKey: queryKeys.apiKeys,
    queryFn: ({ signal }) => api.listApiKeys(signal),
  });
}

export function useIntegrationsQuery() {
  return useQuery({
    queryKey: queryKeys.integrations,
    queryFn: ({ signal }) => api.integrationStatus(signal),
  });
}

export function useCheckCursorHooksMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.checkCursorHooks(),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.integrations });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

export function useReplayOfflineQueueMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.replayOfflineQueue(),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: queryKeys.integrations });
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useDatabaseBackupInfoQuery(destinationDirectory?: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.databaseBackupInfo(destinationDirectory),
    queryFn: ({ signal }) => api.databaseBackupInfo(destinationDirectory, signal),
    enabled,
  });
}

export function useBackupDatabaseMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (destinationDirectory?: string) => api.backupDatabase(destinationDirectory),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['database-backup-info'] });
    },
  });
}

export function useRestoreDatabaseMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (sourceFilePath: string) => api.restoreDatabase(sourceFilePath),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['database-backup-info'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: queryKeys.settings });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useRestoreDatabaseUploadMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => api.restoreDatabaseUpload(file),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['database-backup-info'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: queryKeys.settings });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useUpdateSettingsMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateSettingsRequest) => api.updateSettings(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.settings });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
    },
  });
}

export function useFetchCursorTokenRatesMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.fetchCursorTokenRates(),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.settings });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useCreateApiKeyMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateApiKeyRequestDto) => api.createApiKey(body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: queryKeys.apiKeys }),
  });
}

export function useRevokeApiKeyMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.revokeApiKey(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: queryKeys.apiKeys }),
  });
}

export function useTimesheetCategoriesQuery(activeOnly?: boolean) {
  return useQuery({
    queryKey: queryKeys.timesheetCategories(activeOnly),
    queryFn: ({ signal }) => api.listTimesheetCategories(activeOnly, signal),
  });
}

export function useCreateTimesheetCategoryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTimesheetCategoryRequest) => api.createTimesheetCategory(body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['timesheet-categories'] }),
  });
}

export function useUpdateTimesheetCategoryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateTimesheetCategoryRequest }) =>
      api.updateTimesheetCategory(id, body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['timesheet-categories'] }),
  });
}

export function useDeleteTimesheetCategoryMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.deleteTimesheetCategory(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['timesheet-categories'] }),
  });
}

export function useReconciliationMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ReconciliationRequestDto) => api.runReconciliation(body),
    onSuccess: (data) => {
      if (data.dryRun) {
        return;
      }
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['imported-usage'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useDeleteUnallocatedUsageMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ fromUtc, toUtc }: { fromUtc: string; toUtc: string }) =>
      api.deleteUnallocatedUsage(fromUtc, toUtc),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['imported-usage'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useAllocateUsageMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AllocationRequestDto) => api.allocateUsage(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['imported-usage'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useAllocateUsageToClosestPromptMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (usageRecordId: string) => api.allocateUsageToClosestPrompt(usageRecordId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['imported-usage'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useAssignActivityMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AssignActivityRequestDto) => api.assignActivity(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: queryKeys.projects });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useDeleteActivityMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: DeleteActivityRequestDto) => api.deleteActivity(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: queryKeys.projects });
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}

export function useExportMutation() {
  return useMutation({
    mutationFn: (body: ExportRequestDto) => api.exportReport(body),
  });
}

export function useImportUploadMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (args: {
      file: File;
      dryRun?: boolean;
      force?: boolean;
      columnMappings?: Record<string, string>;
      timezone?: string;
    }) => api.importCursorUpload(args.file, args),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['unallocated'] });
      void qc.invalidateQueries({ queryKey: ['imported-usage'] });
      void qc.invalidateQueries({ queryKey: queryKeys.status });
      void qc.invalidateQueries({ queryKey: ['reports'] });
    },
  });
}
