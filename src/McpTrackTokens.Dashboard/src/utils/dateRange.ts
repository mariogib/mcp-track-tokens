import type { RangePreset } from '@lunarq/frontend-shared/utils';
import {
  currentUtcYearMonth,
  parseRangePreset,
} from '@lunarq/frontend-shared/utils';
import { lastDaysRange, monthBoundsUtc } from './format';

export type { RangePreset };
export { currentUtcYearMonth, parseRangePreset };

export type ResolvedRange = {
  fromUtc: string;
  toUtc: string;
  label: string;
  preset: RangePreset;
};

/** Parse YYYY-MM-DD as UTC day start/end. */
export function parseUtcDateInput(value: string, endOfDay = false): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const [y, m, d] = value.split('-').map(Number);
  const date = endOfDay
    ? new Date(Date.UTC(y, m - 1, d, 23, 59, 59, 999))
    : new Date(Date.UTC(y, m - 1, d, 0, 0, 0, 0));
  if (Number.isNaN(date.getTime())) return null;
  return date.toISOString();
}

export function toDateInputValue(isoUtc: string): string {
  const date = new Date(isoUtc);
  if (Number.isNaN(date.getTime())) return '';
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, '0');
  const d = String(date.getUTCDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function parseYearParam(value: string | null | undefined): number | null {
  if (!value || !/^\d{4}$/.test(value)) return null;
  const year = Number(value);
  if (year < 2000 || year > 2100) return null;
  return year;
}

export function parseMonthParam(value: string | null | undefined): number | null {
  if (!value || !/^\d{1,2}$/.test(value)) return null;
  const month = Number(value);
  if (month < 1 || month > 12) return null;
  return month;
}

export function resolveRange(
  preset: RangePreset,
  fromDate?: string | null,
  toDate?: string | null,
  year?: number | null,
  month?: number | null,
): ResolvedRange {
  if (preset === 'custom' || (fromDate && toDate)) {
    const fromUtc = parseUtcDateInput(fromDate ?? '', false);
    const toUtc = parseUtcDateInput(toDate ?? '', true);
    if (fromUtc && toUtc) {
      const start = new Date(fromUtc).getTime();
      const end = new Date(toUtc).getTime();
      if (start <= end) {
        return {
          fromUtc,
          toUtc,
          label: `${fromDate} → ${toDate}`,
          preset: 'custom',
        };
      }
    }
    // Fall through to 30d if custom dates are incomplete/invalid.
  }

  if (preset === 'month') {
    const defaults = currentUtcYearMonth();
    const y = year != null && year >= 2000 && year <= 2100 ? year : defaults.year;
    const m = month != null && month >= 1 && month <= 12 ? month : defaults.month;
    const bounds = monthBoundsUtc(y, m);
    return {
      ...bounds,
      label: `${y}-${String(m).padStart(2, '0')}`,
      preset: 'month',
    };
  }

  const days = preset === '7d' ? 7 : preset === '90d' ? 90 : 30;
  return {
    ...lastDaysRange(days),
    label: `Last ${days} days`,
    preset: preset === '7d' || preset === '90d' ? preset : '30d',
  };
}

/** Inclusive UTC calendar-month bounds as YYYY-MM-DD inputs for custom range. */
export function monthDateInputs(year: number, month: number): { from: string; to: string } {
  const mm = String(month).padStart(2, '0');
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return {
    from: `${year}-${mm}-01`,
    to: `${year}-${mm}-${String(lastDay).padStart(2, '0')}`,
  };
}
