import { api } from '../api/client';
import type {
  ProjectActivityReport,
  ProjectCostReport,
  ProjectDetailDto,
  ProjectTokenCostEstimate,
  PromptEventDto,
  SessionDto,
  TimesheetEntryDto,
  UsageSummaryDto,
} from '../api/types';
import { REMOTE_BROWSE_EXPORT_CAP } from '../components/RemoteAnalysisDetailBrowse';
import {
  formatCurrency,
  formatDateTime,
  formatDay,
  formatDurationMs,
  formatDurationSeconds,
  formatNumber,
  millisecondsToMinutesExact,
} from './format';
import { sessionDurationMs, timesheetEntryDurationMs } from './duration';
import { downloadMultiSheetExcel, type ExcelSheetSpec } from './multiSheetExcelExport';

type PagedFetch<T> = (pageIndex: number, pageSize: number) => Promise<{
  items: T[];
  totalCount: number;
}>;

async function fetchAllPages<T>(
  fetchPage: PagedFetch<T>,
  cap = REMOTE_BROWSE_EXPORT_CAP,
): Promise<T[]> {
  const pageSize = 100;
  const rows: T[] = [];
  let pageIndex = 0;

  while (rows.length < cap) {
    const page = await fetchPage(pageIndex, pageSize);
    if (page.items.length === 0) {
      break;
    }
    rows.push(...page.items);
    if (rows.length >= page.totalCount || page.items.length < pageSize) {
      break;
    }
    pageIndex += 1;
  }

  return rows.slice(0, cap);
}

function metricSheet(
  sheetName: string,
  rows: Array<{ metric: string; value: string | number }>,
): ExcelSheetSpec {
  return {
    sheetName,
    tableName: sheetName.replace(/\s+/g, ''),
    columns: [
      { header: 'Metric', key: 'metric' },
      { header: 'Value', key: 'value' },
    ],
    data: rows.map((row) => ({ metric: row.metric, value: row.value })),
  };
}

/** Elapsed hours for a timesheet entry (ended − started). Open entries count through now. */
function timesheetHours(entry: TimesheetEntryDto): number | '' {
  const duration = timesheetEntryDurationMs(entry);
  return duration == null ? '' : Math.round((duration / 3_600_000) * 100) / 100;
}

export type ProjectDetailsWorkbookArgs = {
  project: ProjectDetailDto;
  fromUtc: string;
  toUtc: string;
  activity?: ProjectActivityReport | null;
  usage?: UsageSummaryDto | null;
  cost?: ProjectCostReport | null;
  tokenCost?: ProjectTokenCostEstimate | null;
};

/** Build and download a multi-sheet workbook for included project details tabs. */
export async function exportProjectDetailsWorkbook(
  args: ProjectDetailsWorkbookArgs,
): Promise<void> {
  const { project, fromUtc, toUtc, activity, usage, cost, tokenCost } = args;
  const currency = cost?.currency ?? tokenCost?.currency ?? usage?.currency ?? project.currency;
  const byDay = [...(activity?.byDay ?? [])].sort((a, b) => a.day.localeCompare(b.day));
  const reportedTotalCost = cost?.totalAiCost ?? 0;
  const calculatedTotalCost = cost?.calculatedTokenCost ?? 0;
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const tokenTotalForDays = byDay.reduce((sum, row) => sum + (row.totalTokens ?? 0), 0);

  const overviewDaily = byDay.map((row) => {
    const share =
      tokenTotalForDays > 0
        ? ((row.totalTokens ?? 0) / tokenTotalForDays) * displayTotalCost
        : displayTotalCost / Math.max(byDay.length, 1);
    return {
      day: formatDay(row.day),
      prompts: row.promptCount,
      activeMinutes: Math.round(row.activeProjectTimeSeconds / 60),
      agentMinutes: millisecondsToMinutesExact(row.agentDurationMilliseconds),
      tokens: row.totalTokens ?? 0,
      cost: Number(share.toFixed(2)),
    };
  });

  const overviewMetrics = [
    {
      metric: 'Period',
      value: `${formatDateTime(fromUtc)} → ${formatDateTime(toUtc)}`,
    },
    {
      metric: 'Prompts',
      value: formatNumber(activity?.promptCount ?? project.activity?.promptCount),
    },
    {
      metric: 'Agent time',
      value: formatDurationMs(
        activity?.agentDurationMilliseconds ?? project.activity?.agentDurationMilliseconds,
      ),
    },
    {
      metric: 'Active time',
      value: formatDurationSeconds(
        activity?.activeProjectTimeSeconds ?? project.activity?.activeProjectTimeSeconds,
      ),
    },
    {
      metric: 'Total tokens',
      value: formatNumber(cost?.importedTotalTokens ?? usage?.totalTokens ?? 0),
    },
    {
      metric: reportedTotalCost > 0 ? 'Total AI cost' : 'Calculated token cost',
      value: formatCurrency(displayTotalCost || (project.cost?.totalAiCost ?? 0), currency),
    },
    ...overviewDaily.map((row) => ({
      metric: `Day · ${row.day}`,
      value: `prompts ${row.prompts} · active ${row.activeMinutes}m · agent ${row.agentMinutes}m · tokens ${row.tokens} · cost ${row.cost}`,
    })),
    ...(activity?.byBranch ?? []).map((branch) => ({
      metric: `Branch · ${branch.name || '(none)'}`,
      value: formatNumber(branch.promptCount),
    })),
    ...(cost?.byModel ?? [])
      .map((model) => ({
        name: model.name || 'Unknown',
        reported: model.usageBasedCost + model.subscriptionAllocation,
        calculated: model.calculatedTokenCost ?? 0,
      }))
      .filter((model) => model.reported > 0 || model.calculated > 0)
      .flatMap((model) => {
        const rows: Array<{ metric: string; value: string }> = [];
        if (model.reported > 0) {
          rows.push({
            metric: `Cost by model · ${model.name}`,
            value: formatCurrency(model.reported, currency),
          });
        }
        if (model.calculated > 0) {
          rows.push({
            metric: `Calculated cost by model · ${model.name}`,
            value: formatCurrency(model.calculated, currency),
          });
        }
        return rows;
      }),
  ];

  const [prompts, sessions, timesheetEntries] = await Promise.all([
    fetchAllPages<PromptEventDto>((pageIndex, pageSize) =>
      api.getProjectPromptsPaged(project.id, { fromUtc, toUtc, pageIndex, pageSize }),
    ),
    fetchAllPages<SessionDto>((pageIndex, pageSize) =>
      api.getProjectSessionsPaged(project.id, { fromUtc, toUtc, pageIndex, pageSize }),
    ),
    fetchAllPages<TimesheetEntryDto>((pageIndex, pageSize) =>
      api.getProjectTimesheetEntriesPaged(project.id, { fromUtc, toUtc, pageIndex, pageSize }),
    ),
  ]);

  const sheets: ExcelSheetSpec[] = [
    metricSheet('Overview', overviewMetrics),
    {
      sheetName: 'Activity',
      columns: [
        { header: 'Day', key: 'day' },
        { header: 'Prompts', key: 'promptCount' },
        { header: 'Agent runs', key: 'agentRuns' },
        { header: 'Agent duration (ms)', key: 'agentDurationMilliseconds' },
        { header: 'Active time (s)', key: 'activeProjectTimeSeconds' },
        { header: 'Sessions', key: 'sessionCount' },
        { header: 'Timesheets', key: 'timesheetEntryCount' },
        { header: 'Timesheet duration (s)', key: 'timesheetDurationSeconds' },
      ],
      data: (activity?.byDay ?? []).map((row) => ({
        day: formatDay(row.day),
        promptCount: row.promptCount,
        agentRuns: row.agentRuns,
        agentDurationMilliseconds: row.agentDurationMilliseconds,
        activeProjectTimeSeconds: row.activeProjectTimeSeconds,
        sessionCount: row.sessionCount,
        timesheetEntryCount: row.timesheetEntryCount ?? 0,
        timesheetDurationSeconds: row.timesheetDurationSeconds ?? 0,
      })),
    },
    {
      sheetName: 'Prompts',
      columns: [
        { header: 'Time', key: 'timestampUtc' },
        { header: 'Type', key: 'eventType' },
        { header: 'Editor', key: 'editor' },
        { header: 'Model', key: 'model' },
        { header: 'Branch', key: 'branch' },
        { header: 'Status', key: 'status' },
        { header: 'Duration (ms)', key: 'durationMilliseconds' },
        { header: 'Linked usages', key: 'linkedUsageCount' },
        { header: 'Total tokens', key: 'totalTokens' },
        { header: 'Cost', key: 'reportedCost' },
        { header: 'Calculated cost', key: 'calculatedTokenCost' },
      ],
      data: prompts.map((prompt) => ({
        timestampUtc: formatDateTime(prompt.timestampUtc),
        eventType: prompt.eventType,
        editor: prompt.editor ?? '',
        model: prompt.model ?? '',
        branch: prompt.branch ?? '',
        status: prompt.status ?? '',
        durationMilliseconds: prompt.durationMilliseconds ?? '',
        linkedUsageCount: prompt.linkedUsageCount ?? '',
        totalTokens: prompt.totalTokens ?? '',
        reportedCost: prompt.reportedCost ?? '',
        calculatedTokenCost: prompt.calculatedTokenCost ?? '',
      })),
    },
    (() => {
      const rows = sessions.map((session) => {
        const durationMs = sessionDurationMs(session);
        return {
          id: session.id,
          editor: session.editor ?? '',
          startedAtUtc: formatDateTime(session.startedAtUtc),
          endedAtUtc: formatDateTime(session.endedAtUtc),
          hours:
            durationMs == null ? ('' as const) : Math.round((durationMs / 3_600_000) * 100) / 100,
          branch: session.branch ?? '',
          status: session.status || (session.isActive ? 'Active' : 'Closed'),
        };
      });
      const totalHours =
        Math.round(
          rows.reduce((sum, row) => sum + (typeof row.hours === 'number' ? row.hours : 0), 0) * 100,
        ) / 100;
      return {
        sheetName: 'Sessions',
        columns: [
          { header: 'Session', key: 'id' },
          { header: 'Editor', key: 'editor' },
          { header: 'Started', key: 'startedAtUtc' },
          { header: 'Ended', key: 'endedAtUtc' },
          { header: 'Hours', key: 'hours' },
          { header: 'Branch', key: 'branch' },
          { header: 'Status', key: 'status' },
        ],
        data: [
          ...rows,
          {
            id: '(Total)',
            editor: '',
            startedAtUtc: '',
            endedAtUtc: '',
            hours: totalHours,
            branch: '',
            status: '',
          },
        ],
      };
    })(),
    (() => {
      const rows = timesheetEntries.map((entry) => ({
        categoryName: entry.categoryName ?? '',
        startedAtUtc: formatDateTime(entry.startedAtUtc),
        endedAtUtc: formatDateTime(entry.endedAtUtc),
        hours: timesheetHours(entry),
        notes: entry.notes ?? '',
        status: entry.isOpen ? 'Open' : 'Closed',
      }));
      const totalHours =
        Math.round(
          rows.reduce((sum, row) => sum + (typeof row.hours === 'number' ? row.hours : 0), 0) * 100,
        ) / 100;
      return {
        sheetName: 'Timesheet',
        columns: [
          { header: 'Category', key: 'categoryName' },
          { header: 'Started', key: 'startedAtUtc' },
          { header: 'Ended', key: 'endedAtUtc' },
          { header: 'Hours', key: 'hours' },
          { header: 'Notes', key: 'notes' },
          { header: 'Status', key: 'status' },
        ],
        data: [
          ...rows,
          {
            categoryName: '(Total)',
            startedAtUtc: '',
            endedAtUtc: '',
            hours: totalHours,
            notes: '',
            status: '',
          },
        ],
      };
    })(),
    metricSheet('Usage', [
      { metric: 'Total tokens', value: formatNumber(usage?.totalTokens) },
      { metric: 'Input tokens', value: formatNumber(usage?.inputTokens) },
      { metric: 'Output tokens', value: formatNumber(usage?.outputTokens) },
      { metric: 'Cached input', value: formatNumber(usage?.cachedInputTokens) },
      { metric: 'Reasoning', value: formatNumber(usage?.reasoningTokens) },
      { metric: 'Reported cost', value: formatCurrency(usage?.reportedCost, usage?.currency) },
      { metric: 'Requests', value: formatNumber(usage?.requestCount) },
    ]),
    {
      sheetName: 'Cost',
      columns: [
        { header: 'Model', key: 'name' },
        { header: 'Usage cost', key: 'usageBasedCost' },
        { header: 'Subscription', key: 'subscriptionAllocation' },
        { header: 'Token cost', key: 'calculatedTokenCost' },
        { header: 'Prompts', key: 'promptCount' },
        { header: 'Other providers', key: 'otherProviderCost' },
        { header: 'Unallocated', key: 'unallocatedCost' },
        { header: 'Total AI cost', key: 'totalAiCost' },
      ],
      data: [
        {
          name: '(Summary)',
          usageBasedCost: cost?.usageBasedCursorCost ?? '',
          subscriptionAllocation: cost?.subscriptionAllocation ?? '',
          calculatedTokenCost: cost?.calculatedTokenCost ?? '',
          promptCount: '',
          otherProviderCost: cost?.otherProviderCost ?? '',
          unallocatedCost: cost?.unallocatedCost ?? '',
          totalAiCost: cost?.totalAiCost ?? '',
        },
        ...(cost?.byModel ?? []).map((model) => ({
          name: model.name,
          usageBasedCost: model.usageBasedCost,
          subscriptionAllocation: model.subscriptionAllocation,
          calculatedTokenCost: model.calculatedTokenCost ?? 0,
          promptCount: model.promptCount,
          otherProviderCost: '',
          unallocatedCost: '',
          totalAiCost: '',
        })),
      ],
    },
    {
      sheetName: 'Token Costs',
      columns: [
        { header: 'Model', key: 'model' },
        { header: 'Rate used', key: 'rateSource' },
        { header: 'Input', key: 'inputTokens' },
        { header: 'Output', key: 'outputTokens' },
        { header: 'Cached', key: 'cachedInputTokens' },
        { header: 'Cache write', key: 'cacheWriteTokens' },
        { header: 'Reasoning', key: 'reasoningTokens' },
        { header: 'Total tokens', key: 'totalTokens' },
        { header: 'Estimated', key: 'estimatedCost' },
        { header: 'Reported', key: 'reportedCost' },
      ],
      data: [
        {
          model: '(Summary)',
          rateSource: `${tokenCost?.rateCardModelCount ?? 0} rate card models`,
          inputTokens: tokenCost?.inputTokens ?? '',
          outputTokens: tokenCost?.outputTokens ?? '',
          cachedInputTokens: tokenCost?.cachedInputTokens ?? '',
          cacheWriteTokens: tokenCost?.cacheWriteTokens ?? '',
          reasoningTokens: tokenCost?.reasoningTokens ?? '',
          totalTokens: tokenCost?.totalTokens ?? '',
          estimatedCost: tokenCost?.estimatedCost ?? '',
          reportedCost: tokenCost?.reportedCost ?? '',
        },
        ...(tokenCost?.byModel ?? []).map((row) => ({
          model: row.model,
          rateSource: row.rateSource,
          inputTokens: row.inputTokens,
          outputTokens: row.outputTokens,
          cachedInputTokens: row.cachedInputTokens,
          cacheWriteTokens: row.cacheWriteTokens ?? 0,
          reasoningTokens: row.reasoningTokens,
          totalTokens: row.totalTokens,
          estimatedCost: row.estimatedCost,
          reportedCost: row.reportedCost,
        })),
      ],
    },
  ];

  if (sheets.length === 0) {
    throw new Error('Excel export requires at least one sheet.');
  }

  downloadMultiSheetExcel({
    filename: `project-${project.id}-details`,
    timestamp: new Date().toISOString(),
    sheets,
  });
}
