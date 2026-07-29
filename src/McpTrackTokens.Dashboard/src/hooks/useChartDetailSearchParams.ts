import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  currentUtcYearMonth,
  parseMonthParam,
  parseRangePreset,
  parseYearParam,
  resolveRange,
  toDateInputValue,
  type RangePreset,
  type ResolvedRange,
} from '../utils/dateRange';

export type ChartDetailSearchState = {
  range: ResolvedRange;
  fromDate: string;
  toDate: string;
  rangeYear: number | null;
  rangeMonth: number | null;
  modelFilter: string;
  dayFilter: string;
  projectFilter: string;
  branchFilter: string;
  updateParams: (patch: Record<string, string | null>) => void;
  onPresetChange: (next: RangePreset) => void;
  onYearMonthChange: (year: number, month: number) => void;
};

/** Shared URL-param plumbing for overview and project chart detail pages. */
export function useChartDetailSearchParams(): ChartDetailSearchState {
  const [searchParams, setSearchParams] = useSearchParams();

  const preset = parseRangePreset(searchParams.get('range'));
  const fromDate = searchParams.get('from') ?? '';
  const toDate = searchParams.get('to') ?? '';
  const rangeYear = parseYearParam(searchParams.get('year'));
  const rangeMonth = parseMonthParam(searchParams.get('month'));
  const modelFilter = searchParams.get('model') ?? '';
  const dayFilter = searchParams.get('day') ?? '';
  const projectFilter = searchParams.get('project') ?? '';
  const branchFilter = searchParams.get('branch') ?? '';

  const range = useMemo(
    () =>
      resolveRange(
        preset === 'custom' || (fromDate && toDate) ? 'custom' : preset,
        fromDate,
        toDate,
        rangeYear,
        rangeMonth,
      ),
    [preset, fromDate, toDate, rangeYear, rangeMonth],
  );

  const updateParams = useCallback(
    (patch: Record<string, string | null>) => {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          for (const [key, value] of Object.entries(patch)) {
            if (value == null || value === '') next.delete(key);
            else next.set(key, value);
          }
          return next;
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  const onPresetChange = useCallback(
    (next: RangePreset) => {
      if (next === 'custom') {
        const defaults = resolveRange('30d');
        updateParams({
          range: 'custom',
          from: toDateInputValue(defaults.fromUtc),
          to: toDateInputValue(defaults.toUtc),
          year: null,
          month: null,
        });
        return;
      }
      if (next === 'month') {
        const defaults = currentUtcYearMonth();
        updateParams({
          range: 'month',
          year: String(defaults.year),
          month: String(defaults.month),
          from: null,
          to: null,
        });
        return;
      }
      updateParams({ range: next, from: null, to: null, year: null, month: null });
    },
    [updateParams],
  );

  const onYearMonthChange = useCallback(
    (nextYear: number, nextMonth: number) => {
      updateParams({
        range: 'month',
        year: String(nextYear),
        month: String(nextMonth),
        from: null,
        to: null,
      });
    },
    [updateParams],
  );

  return {
    range,
    fromDate,
    toDate,
    rangeYear,
    rangeMonth,
    modelFilter,
    dayFilter,
    projectFilter,
    branchFilter,
    updateParams,
    onPresetChange,
    onYearMonthChange,
  };
}
