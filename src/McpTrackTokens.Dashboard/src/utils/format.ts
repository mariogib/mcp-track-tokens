export function formatDurationMs(ms: number | null | undefined): string {
  const value = Math.max(0, Math.round(ms ?? 0));
  const totalSeconds = Math.floor(value / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

/** Exact minutes from accumulated milliseconds (no rounding — for charts / further math). */
export function millisecondsToMinutesExact(ms: number | null | undefined): number {
  return Math.max(0, ms ?? 0) / 60_000;
}

/**
 * Whole minutes from accumulated milliseconds.
 * Round only after the millisecond total is complete — do not round per-event or mid-sum.
 */
export function millisecondsToMinutes(ms: number | null | undefined): number {
  return Math.round(millisecondsToMinutesExact(ms));
}

export function formatDurationSeconds(seconds: number | null | undefined): string {
  return formatDurationMs((seconds ?? 0) * 1000);
}

export function formatCurrency(
  amount: number | null | undefined,
  currency = 'USD',
  fractionDigits = 2,
): string {
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency || 'USD',
      minimumFractionDigits: fractionDigits,
      maximumFractionDigits: fractionDigits,
    }).format(amount ?? 0);
  } catch {
    return `${(amount ?? 0).toFixed(fractionDigits)} ${currency}`;
  }
}

export function formatNumber(value: number | null | undefined): string {
  return new Intl.NumberFormat().format(value ?? 0);
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).format(date);
}

export function formatDay(value?: string | null): string {
  if (!value) return '—';
  const date = new Date(value.includes('T') ? value : `${value}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(date);
}

export function monthBoundsUtc(year: number, month: number): { fromUtc: string; toUtc: string } {
  const from = new Date(Date.UTC(year, month - 1, 1, 0, 0, 0));
  const to = new Date(Date.UTC(year, month, 1, 0, 0, 0));
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function lastDaysRange(days: number): { fromUtc: string; toUtc: string } {
  const to = new Date();
  const from = new Date(to.getTime() - days * 24 * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function todayStartUtc(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate())).toISOString();
}
