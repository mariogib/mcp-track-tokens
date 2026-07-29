import type { SessionDto, TimesheetEntryDto } from '../api/types';

export function sessionDurationMs(session: SessionDto): number | null {
  if (!session.startedAtUtc || !session.endedAtUtc) return null;
  const duration = new Date(session.endedAtUtc).getTime() - new Date(session.startedAtUtc).getTime();
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
