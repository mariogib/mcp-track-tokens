import { describe, expect, it } from 'vitest';
import type { SessionDto, TimesheetEntryDto } from '../api/types';
import {
  mergeIntervals,
  sessionsWithinTimesheetPeriods,
  sessionsWithinTimeRange,
} from './duration';

function session(
  id: string,
  startedAtUtc: string,
  endedAtUtc: string | null,
): SessionDto {
  return {
    id,
    startedAtUtc,
    endedAtUtc,
    isActive: endedAtUtc == null,
  };
}

function timesheet(
  id: string,
  startedAtUtc: string,
  endedAtUtc: string | null,
): TimesheetEntryDto {
  return {
    id,
    projectId: 'p1',
    categoryId: 'c1',
    categoryName: 'Work',
    startedAtUtc,
    endedAtUtc,
    notes: null,
    isOpen: endedAtUtc == null,
    createdAtUtc: startedAtUtc,
    updatedAtUtc: startedAtUtc,
  };
}

describe('mergeIntervals', () => {
  it('merges overlapping intervals', () => {
    expect(
      mergeIntervals([
        { startMs: 0, endMs: 100 },
        { startMs: 50, endMs: 150 },
        { startMs: 200, endMs: 250 },
      ]),
    ).toEqual([
      { startMs: 0, endMs: 150 },
      { startMs: 200, endMs: 250 },
    ]);
  });
});

describe('sessionsWithinTimesheetPeriods', () => {
  const now = Date.parse('2026-07-30T12:00:00.000Z');

  it('clips session duration to the timesheet period', () => {
    const rows = sessionsWithinTimesheetPeriods(
      [
        session(
          's1',
          '2026-07-30T08:00:00.000Z',
          '2026-07-30T11:00:00.000Z',
        ),
      ],
      [timesheet('t1', '2026-07-30T09:00:00.000Z', '2026-07-30T10:00:00.000Z')],
      now,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].startUtc).toBe('2026-07-30T09:00:00.000Z');
    expect(rows[0].endUtc).toBe('2026-07-30T10:00:00.000Z');
    expect(rows[0].durationMs).toBe(3_600_000);
  });

  it('excludes sessions outside timesheet periods', () => {
    const rows = sessionsWithinTimesheetPeriods(
      [session('s1', '2026-07-30T06:00:00.000Z', '2026-07-30T07:00:00.000Z')],
      [timesheet('t1', '2026-07-30T09:00:00.000Z', '2026-07-30T10:00:00.000Z')],
      now,
    );
    expect(rows).toHaveLength(0);
  });

  it('does not double-count overlapping timesheet periods', () => {
    const rows = sessionsWithinTimesheetPeriods(
      [session('s1', '2026-07-30T08:00:00.000Z', '2026-07-30T12:00:00.000Z')],
      [
        timesheet('t1', '2026-07-30T09:00:00.000Z', '2026-07-30T11:00:00.000Z'),
        timesheet('t2', '2026-07-30T10:00:00.000Z', '2026-07-30T12:00:00.000Z'),
      ],
      now,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].durationMs).toBe(3 * 3_600_000);
    expect(rows[0].startUtc).toBe('2026-07-30T09:00:00.000Z');
    expect(rows[0].endUtc).toBe('2026-07-30T12:00:00.000Z');
  });
});

describe('sessionsWithinTimeRange', () => {
  it('clips sessions to the given day range', () => {
    const rows = sessionsWithinTimeRange(
      [
        {
          id: 's1',
          startedAtUtc: '2026-07-30T22:00:00.000Z',
          endedAtUtc: '2026-07-31T02:00:00.000Z',
          isActive: false,
        },
      ],
      '2026-07-30T00:00:00.000Z',
      '2026-07-30T23:59:59.999Z',
    );
    expect(rows).toHaveLength(1);
    expect(rows[0].startUtc).toBe('2026-07-30T22:00:00.000Z');
    expect(rows[0].endUtc).toBe('2026-07-30T23:59:59.999Z');
    expect(rows[0].durationMs).toBe(
      Date.parse('2026-07-30T23:59:59.999Z') - Date.parse('2026-07-30T22:00:00.000Z'),
    );
  });
});
