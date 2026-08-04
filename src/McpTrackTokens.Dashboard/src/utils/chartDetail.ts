import type { DailyActivityRow, NamedMetricRow } from '../api/types';
import { lineValueKey } from '../data/chartDefs';
import {
  formatCurrency,
  formatDay,
  formatNumber,
  millisecondsToMinutesExact,
} from './format';

export type DaySeriesPoint = {
  dayKey: string;
  day: string;
  prompts: number;
  activeMinutes: number;
  agentDurationMilliseconds: number;
  agentMinutes: number;
  tokens: number;
  cost: number;
};

export type NamedCostPoint = {
  name: string;
  /** Primary chart value (reported cost or calculated token cost). */
  cost: number;
  /** Rate-card calculated token cost (when different from `cost`). */
  calculatedTokenCost?: number;
  tokens?: number;
  rateSource?: string;
  /** Currency units per 1,000,000 tokens from the Settings rate card. */
  inputPerMillion?: number;
  outputPerMillion?: number;
  cacheReadPerMillion?: number;
  cacheWritePerMillion?: number;
};

export type NamedPromptPoint = {
  name: string;
  prompts: number;
  projectId?: string;
};

export type ChartStats = {
  total: number;
  avg: number;
  max: number;
  count: number;
};

export function buildDaySeries(
  byDay: DailyActivityRow[],
  displayTotalCost: number,
): DaySeriesPoint[] {
  const chronological = [...byDay].sort((a, b) => a.day.localeCompare(b.day));
  const tokenTotal = chronological.reduce((sum, row) => sum + (row.totalTokens ?? 0), 0);
  return chronological.map((row) => {
    const costShare =
      tokenTotal > 0
        ? ((row.totalTokens ?? 0) / tokenTotal) * displayTotalCost
        : displayTotalCost / Math.max(chronological.length, 1);
    return {
      dayKey: row.day,
      day: formatDay(row.day),
      prompts: row.promptCount,
      activeMinutes: Math.round(row.activeProjectTimeSeconds / 60),
      agentDurationMilliseconds: row.agentDurationMilliseconds,
      agentMinutes: millisecondsToMinutesExact(row.agentDurationMilliseconds),
      tokens: row.totalTokens ?? 0,
      cost: Number(costShare.toFixed(4)),
    };
  });
}

export function buildModelCostSeries(
  byModel: NamedMetricRow[],
  modelFilter: string,
): NamedCostPoint[] {
  const rows = byModel
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.usageBasedCost + m.subscriptionAllocation,
      calculatedTokenCost: m.calculatedTokenCost ?? 0,
    }))
    .filter((m) => m.cost > 0);
  if (!modelFilter) return rows;
  return rows.filter((m) => m.name === modelFilter);
}

export function buildModelCalculatedSeries(
  byModel: NamedMetricRow[],
  modelFilter: string,
): NamedCostPoint[] {
  const rows = byModel
    .map((m) => ({
      name: m.name || 'Unknown',
      cost: m.calculatedTokenCost ?? 0,
      calculatedTokenCost: m.calculatedTokenCost ?? 0,
    }))
    .filter((m) => m.cost > 0);
  if (!modelFilter) return rows;
  return rows.filter((m) => m.name === modelFilter);
}

type TokenRateRow = {
  model: string;
  rateSource: string;
  totalTokens: number;
  inputPerMillion: number;
  outputPerMillion: number;
  cacheReadPerMillion: number;
  cacheWritePerMillion?: number;
};

/** Attach Settings rate-card token prices to calculated-cost model rows. */
export function enrichModelCostWithTokenRates(
  points: NamedCostPoint[],
  tokenModels: TokenRateRow[],
): NamedCostPoint[] {
  if (tokenModels.length === 0) return points;
  const byKey = new Map(
    tokenModels.map((row) => [(row.model || 'Unknown').trim().toLowerCase(), row]),
  );
  return points.map((point) => {
    const rate = byKey.get(point.name.trim().toLowerCase());
    if (!rate) return point;
    return {
      ...point,
      tokens: rate.totalTokens,
      rateSource: rate.rateSource,
      inputPerMillion: rate.inputPerMillion,
      outputPerMillion: rate.outputPerMillion,
      cacheReadPerMillion: rate.cacheReadPerMillion,
      cacheWritePerMillion: rate.cacheWritePerMillion ?? 0,
    };
  });
}

export function summarize(values: number[]): ChartStats {
  if (!values.length) {
    return { total: 0, avg: 0, max: 0, count: 0 };
  }
  const total = values.reduce((s, v) => s + v, 0);
  return {
    total,
    avg: total / values.length,
    max: Math.max(...values),
    count: values.length,
  };
}

export function computeChartStats(
  chartKey: string,
  data: {
    daySeries: DaySeriesPoint[];
    pieData: NamedCostPoint[];
    barSeries: NamedPromptPoint[];
  },
): ChartStats {
  if (chartKey === 'cost-by-model' || chartKey === 'calculated-cost-by-model') {
    return summarize(data.pieData.map((r) => r.cost));
  }
  if (chartKey === 'activity-by-project' || chartKey === 'activity-by-branch') {
    return summarize(data.barSeries.map((r) => r.prompts));
  }
  if (chartKey === 'agent-duration-day') {
    const msValues = data.daySeries.map((r) => r.agentDurationMilliseconds);
    if (!msValues.length) {
      return { total: 0, avg: 0, max: 0, count: 0 };
    }
    const totalMs = msValues.reduce((s, v) => s + v, 0);
    return {
      total: millisecondsToMinutesExact(totalMs),
      avg: millisecondsToMinutesExact(totalMs / msValues.length),
      max: millisecondsToMinutesExact(Math.max(...msValues)),
      count: msValues.length,
    };
  }
  const key = lineValueKey(chartKey) as keyof DaySeriesPoint;
  return summarize(data.daySeries.map((r) => Number(r[key] ?? 0)));
}

export function formatChartStat(value: number, chartKey: string, currency: string): string {
  if (
    chartKey === 'cost-day' ||
    chartKey === 'cost-by-model' ||
    chartKey === 'calculated-cost-by-model'
  ) {
    return formatCurrency(value, currency);
  }
  if (chartKey === 'tokens-day') {
    return formatNumber(Math.round(value));
  }
  if (value % 1 !== 0) {
    return formatNumber(Number(value.toFixed(1)));
  }
  return formatNumber(value);
}

export function resolveDisplayCost(reportedTotalCost: number, calculatedTotalCost: number) {
  const displayTotalCost = reportedTotalCost > 0 ? reportedTotalCost : calculatedTotalCost;
  const usingCalculatedCost = reportedTotalCost <= 0 && calculatedTotalCost > 0;
  return { displayTotalCost, usingCalculatedCost };
}
