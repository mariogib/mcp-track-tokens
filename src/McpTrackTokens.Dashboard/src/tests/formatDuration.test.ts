import { describe, expect, it } from 'vitest';
import { millisecondsToMinutes, millisecondsToMinutesExact } from '../utils/format';

describe('millisecondsToMinutes', () => {
  it('converts from milliseconds only after the total is known', () => {
    // 20s + 20s + 20s = 60s = 1 min when converted once at the end
    const totalMs = 20_000 + 20_000 + 20_000;
    expect(millisecondsToMinutes(totalMs)).toBe(1);
    // Early per-chunk rounding would wrongly yield 0 + 0 + 0 = 0
    expect(
      millisecondsToMinutes(20_000) +
        millisecondsToMinutes(20_000) +
        millisecondsToMinutes(20_000),
    ).toBe(0);
  });

  it('keeps exact fractional minutes for charts', () => {
    expect(millisecondsToMinutesExact(90_000)).toBe(1.5);
    expect(millisecondsToMinutesExact(30_000)).toBe(0.5);
  });
});
