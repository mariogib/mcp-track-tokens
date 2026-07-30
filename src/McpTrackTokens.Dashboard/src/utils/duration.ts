import type { SessionDto, TimesheetEntryDto } from '../api/types';

export function sessionDurationMs(session: SessionDto, now = Date.now()): number | null {
  if (!session.startedAtUtc) return null;
  const start = new Date(session.startedAtUtc).getTime();
  const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : now;
  const duration = end - start;
  return Number.isFinite(duration) && duration >= 0 ? duration : null;
}

export function timesheetEntryDurationMs(
  entry: TimesheetEntryDto,
  now = Date.now(),
): number | null {
  if (!entry.startedAtUtc) return null;
  const start = new Date(entry.startedAtUtc).getTime();
  const end = entry.endedAtUtc ? new Date(entry.endedAtUtc).getTime() : now;
  const duration = end - start;
  return Number.isFinite(duration) && duration >= 0 ? duration : null;
}

type TimeInterval = { startMs: number; endMs: number };

export type SessionWithinTimesheet = {
  session: SessionDto;
  /** Earliest overlap with a timesheet period. */
  startUtc: string;
  /** Latest overlap with a timesheet period. */
  endUtc: string;
  /** Total milliseconds overlapping timesheet periods (merged). */
  durationMs: number;
};

export type TimesheetWithinRange = {
  entry: TimesheetEntryDto;
  startUtc: string;
  endUtc: string;
  durationMs: number;
};

function toInterval(
  startedAtUtc: string,
  endedAtUtc: string | null | undefined,
  now: number,
): TimeInterval | null {
  const startMs = new Date(startedAtUtc).getTime();
  const endMs = endedAtUtc ? new Date(endedAtUtc).getTime() : now;
  if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || endMs < startMs) {
    return null;
  }
  return { startMs, endMs };
}

/** Merge overlapping / adjacent intervals so concurrent coverage is not double-counted. */
export function mergeIntervals(intervals: TimeInterval[]): TimeInterval[] {
  if (intervals.length === 0) return [];
  const sorted = [...intervals].sort((a, b) => a.startMs - b.startMs);
  const merged: TimeInterval[] = [{ ...sorted[0] }];
  for (let i = 1; i < sorted.length; i++) {
    const current = sorted[i];
    const last = merged[merged.length - 1];
    if (current.startMs <= last.endMs) {
      last.endMs = Math.max(last.endMs, current.endMs);
    } else {
      merged.push({ ...current });
    }
  }
  return merged;
}

function clipInterval(a: TimeInterval, b: TimeInterval): TimeInterval | null {
  const startMs = Math.max(a.startMs, b.startMs);
  const endMs = Math.min(a.endMs, b.endMs);
  if (endMs <= startMs) return null;
  return { startMs, endMs };
}

/**
 * Sessions whose time overlaps any timesheet entry period for the project.
 * Start/end are the clipped overlap bounds; duration is the merged overlap length.
 */
export function sessionsWithinTimesheetPeriods(
  sessions: SessionDto[],
  timesheetEntries: TimesheetEntryDto[],
  now = Date.now(),
): SessionWithinTimesheet[] {
  const timesheetIntervals = mergeIntervals(
    timesheetEntries
      .map((entry) => toInterval(entry.startedAtUtc, entry.endedAtUtc, now))
      .filter((interval): interval is TimeInterval => interval != null),
  );
  return sessionsWithinIntervals(sessions, timesheetIntervals, now);
}

/** Local-calendar day bounds as UTC instants (browser timezone). */
export function dayBoundsLocal(day: string): { fromUtc: string; toUtc: string } {
  const [year, month, dayOfMonth] = day.split('-').map(Number);
  const from = new Date(year, month - 1, dayOfMonth, 0, 0, 0, 0);
  const to = new Date(year, month - 1, dayOfMonth, 23, 59, 59, 999);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

/**
 * Sessions overlapping `[fromUtc, toUtc]`, with start/end/duration clipped to that range.
 */
export function sessionsWithinTimeRange(
  sessions: SessionDto[],
  fromUtc: string,
  toUtc: string,
  now = Date.now(),
): SessionWithinTimesheet[] {
  const range = toInterval(fromUtc, toUtc, now);
  if (!range) return [];
  return sessionsWithinIntervals(sessions, [range], now);
}

/**
 * Timesheet entries overlapping `[fromUtc, toUtc]`, with start/end/duration clipped to that range.
 */
export function timesheetsWithinTimeRange(
  entries: TimesheetEntryDto[],
  fromUtc: string,
  toUtc: string,
  now = Date.now(),
): TimesheetWithinRange[] {
  const range = toInterval(fromUtc, toUtc, now);
  if (!range) return [];

  const rows: TimesheetWithinRange[] = [];
  for (const entry of entries) {
    const entryInterval = toInterval(entry.startedAtUtc, entry.endedAtUtc, now);
    if (!entryInterval) continue;
    const clip = clipInterval(entryInterval, range);
    if (!clip) continue;
    const durationMs = clip.endMs - clip.startMs;
    if (durationMs <= 0) continue;
    rows.push({
      entry,
      startUtc: new Date(clip.startMs).toISOString(),
      endUtc: new Date(clip.endMs).toISOString(),
      durationMs,
    });
  }

  return rows.sort((a, b) => b.startUtc.localeCompare(a.startUtc));
}

function sessionsWithinIntervals(
  sessions: SessionDto[],
  intervals: TimeInterval[],
  now: number,
): SessionWithinTimesheet[] {
  if (intervals.length === 0) return [];

  const rows: SessionWithinTimesheet[] = [];
  for (const session of sessions) {
    const sessionInterval = toInterval(session.startedAtUtc, session.endedAtUtc, now);
    if (!sessionInterval) continue;

    const clips: TimeInterval[] = [];
    for (const interval of intervals) {
      const clip = clipInterval(sessionInterval, interval);
      if (clip) clips.push(clip);
    }
    const mergedClips = mergeIntervals(clips);
    if (mergedClips.length === 0) continue;

    const durationMs = mergedClips.reduce((sum, clip) => sum + (clip.endMs - clip.startMs), 0);
    if (durationMs <= 0) continue;

    const startMs = mergedClips[0].startMs;
    const endMs = mergedClips[mergedClips.length - 1].endMs;
    rows.push({
      session,
      startUtc: new Date(startMs).toISOString(),
      endUtc: new Date(endMs).toISOString(),
      durationMs,
    });
  }

  return rows.sort((a, b) => b.startUtc.localeCompare(a.startUtc));
}
