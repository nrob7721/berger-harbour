import { describe, expect, it } from 'vitest';
import { assignLanes, bookingSpan, nightsSpan } from './timeline';

describe('timeline geometry', () => {
  it('bars run from the middle of the check-in cell to the middle of the check-out cell', () => {
    expect(bookingSpan('2026-11-01', 30, '2026-11-16', '2026-11-20')).toEqual({ left: 15.5, width: 4, clippedStart: false, clippedEnd: false });
  });

  it('clips bars that cross the month edges', () => {
    expect(bookingSpan('2026-11-01', 30, '2026-10-30', '2026-11-02')).toEqual({ left: 0, width: 1.5, clippedStart: true, clippedEnd: false });
    expect(bookingSpan('2026-11-01', 30, '2026-11-29', '2026-12-03')).toEqual({ left: 28.5, width: 1.5, clippedStart: false, clippedEnd: true });
    expect(bookingSpan('2026-11-01', 30, '2026-12-01', '2026-12-03')).toBeNull();
  });

  it('unavailability covers its inclusive nights', () => {
    expect(nightsSpan('2026-11-01', 30, '2026-11-10', '2026-11-10')).toMatchObject({ left: 9.5, width: 1 });
  });

  it('same-day changeovers share a lane; overlaps do not', () => {
    const lanes = assignLanes([
      { start: '2026-11-13', end: '2026-11-16' },
      { start: '2026-11-16', end: '2026-11-20' },
      { start: '2026-11-17', end: '2026-11-19' },
    ]);
    expect(lanes.map((l) => l.lane)).toEqual([0, 0, 1]);
  });
});
