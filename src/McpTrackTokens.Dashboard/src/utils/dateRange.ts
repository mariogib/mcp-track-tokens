import { lastDaysRange, monthBoundsUtc } from './format';

export type RangePreset = '7d' | '30d' | '90d' | 'month' | 'custom';

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

export function resolveRange(
  preset: RangePreset,
  fromDate?: string | null,
  toDate?: string | null,
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
    const now = new Date();
    const year = now.getUTCFullYear();
    const month = now.getUTCMonth() + 1;
    const bounds = monthBoundsUtc(year, month);
    return {
      ...bounds,
      label: `${year}-${String(month).padStart(2, '0')}`,
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

export function parseRangePreset(value: string | null | undefined): RangePreset {
  if (value === '7d' || value === '30d' || value === '90d' || value === 'month' || value === 'custom') {
    return value;
  }
  return '30d';
}
