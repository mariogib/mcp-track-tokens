import type {
  DailyActivityRow,
  NamedMetricRow,
  ProjectActivityReport,
  ProjectCostReport,
} from '../api/types';

export type AggregatedActivity = {
  promptCount: number;
  agentRuns: number;
  agentDurationMilliseconds: number;
  activeProjectTimeSeconds: number;
  byDay: DailyActivityRow[];
  byBranch: NamedMetricRow[];
};

export type AggregatedCost = {
  currency: string;
  importedTotalTokens: number;
  totalAiCost: number;
  calculatedTokenCost: number;
  byModel: NamedMetricRow[];
};

function mergeNamedMetrics(rows: NamedMetricRow[]): NamedMetricRow[] {
  const map = new Map<string, NamedMetricRow>();
  for (const row of rows) {
    const key = row.name || '(none)';
    const existing = map.get(key);
    if (!existing) {
      map.set(key, { ...row, name: key });
      continue;
    }
    map.set(key, {
      ...existing,
      promptCount: existing.promptCount + row.promptCount,
      agentRuns: existing.agentRuns + row.agentRuns,
      agentDurationMilliseconds:
        existing.agentDurationMilliseconds + row.agentDurationMilliseconds,
      activeProjectTimeSeconds:
        existing.activeProjectTimeSeconds + row.activeProjectTimeSeconds,
      usageBasedCost: existing.usageBasedCost + row.usageBasedCost,
      subscriptionAllocation: existing.subscriptionAllocation + row.subscriptionAllocation,
      calculatedTokenCost:
        (existing.calculatedTokenCost ?? 0) + (row.calculatedTokenCost ?? 0),
    });
  }
  return [...map.values()].sort((a, b) => b.promptCount - a.promptCount);
}

export function aggregateActivityReports(
  reports: ProjectActivityReport[],
): AggregatedActivity {
  const byDayMap = new Map<string, DailyActivityRow>();
  let promptCount = 0;
  let agentRuns = 0;
  let agentDurationMilliseconds = 0;
  let activeProjectTimeSeconds = 0;
  const branchRows: NamedMetricRow[] = [];

  for (const report of reports) {
    promptCount += report.promptCount;
    agentRuns += report.agentRuns;
    agentDurationMilliseconds += report.agentDurationMilliseconds;
    activeProjectTimeSeconds += report.activeProjectTimeSeconds;
    branchRows.push(...report.byBranch);

    for (const row of report.byDay) {
      const key = row.day;
      const existing = byDayMap.get(key);
      if (!existing) {
        byDayMap.set(key, {
          ...row,
          projectId: null,
          projectName: null,
          editor: null,
        });
        continue;
      }
      byDayMap.set(key, {
        ...existing,
        promptCount: existing.promptCount + row.promptCount,
        agentRuns: existing.agentRuns + row.agentRuns,
        agentDurationMilliseconds:
          existing.agentDurationMilliseconds + row.agentDurationMilliseconds,
        activeProjectTimeSeconds:
          existing.activeProjectTimeSeconds + row.activeProjectTimeSeconds,
        sessionCount: existing.sessionCount + row.sessionCount,
        timesheetEntryCount: (existing.timesheetEntryCount ?? 0) + (row.timesheetEntryCount ?? 0),
        timesheetDurationSeconds:
          (existing.timesheetDurationSeconds ?? 0) + (row.timesheetDurationSeconds ?? 0),
        totalTokens: (existing.totalTokens ?? 0) + (row.totalTokens ?? 0),
      });
    }
  }

  const byDay = [...byDayMap.values()].sort((a, b) => b.day.localeCompare(a.day));

  return {
    promptCount,
    agentRuns,
    agentDurationMilliseconds,
    activeProjectTimeSeconds,
    byDay,
    byBranch: mergeNamedMetrics(branchRows),
  };
}

export function aggregateCostReports(reports: ProjectCostReport[]): AggregatedCost {
  const modelRows: NamedMetricRow[] = [];
  let importedTotalTokens = 0;
  let totalAiCost = 0;
  let calculatedTokenCost = 0;
  let currency = 'USD';

  for (const report of reports) {
    importedTotalTokens += report.importedTotalTokens;
    totalAiCost += report.totalAiCost;
    calculatedTokenCost += report.calculatedTokenCost ?? 0;
    if (report.currency) currency = report.currency;
    modelRows.push(...report.byModel);
  }

  const byModel = mergeNamedMetrics(modelRows).sort((a, b) => {
    const aCost =
      a.usageBasedCost + a.subscriptionAllocation + (a.calculatedTokenCost ?? 0);
    const bCost =
      b.usageBasedCost + b.subscriptionAllocation + (b.calculatedTokenCost ?? 0);
    return bCost - aCost;
  });

  return {
    currency,
    importedTotalTokens,
    totalAiCost,
    calculatedTokenCost,
    byModel,
  };
}
