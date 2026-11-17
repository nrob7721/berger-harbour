import { describe, expect, it } from 'vitest';
import { isClickableDay, periodForClick, periodTypeOf, stayOverlapsNights, staysOverlap } from './periods';

// Mon 12/10/2026 … Sun 18/10/2026
const days = ['2026-10-12', '2026-10-13', '2026-10-14', '2026-10-15', '2026-10-16', '2026-10-17', '2026-10-18'];

describe('period mapping', () => {
  it('maps every weekday in mid-week mode', () => {
    expect(days.map((d) => periodForClick('Midweek', d)?.start ?? null)).toEqual([
      '2026-10-12', '2026-10-12', '2026-10-12', '2026-10-12', '2026-10-12', null, null,
    ]);
    expect(periodForClick('Midweek', '2026-10-16')).toEqual({ start: '2026-10-12', end: '2026-10-16' });
  });

  it('maps every weekday in weekend mode, Monday selecting the weekend ending that day', () => {
    expect(days.map((d) => periodForClick('Weekend', d)?.start ?? null)).toEqual([
      '2026-10-09', null, null, null, '2026-10-16', '2026-10-16', '2026-10-16',
    ]);
    expect(periodForClick('Weekend', '2026-10-12')).toEqual({ start: '2026-10-09', end: '2026-10-12' });
  });

  it('maps every day to the Fri→Fri week whose nights contain it, starting on a clicked Friday', () => {
    // Mon 12/10 … Sun 18/10/2026: Mon–Thu belong to the week from Fri 09/10; Fri–Sun to the week from Fri 16/10.
    expect(days.map((d) => periodForClick('WeekFri', d)?.start ?? null)).toEqual([
      '2026-10-09', '2026-10-09', '2026-10-09', '2026-10-09', '2026-10-16', '2026-10-16', '2026-10-16',
    ]);
    expect(periodForClick('WeekFri', '2026-10-15')).toEqual({ start: '2026-10-09', end: '2026-10-16' });
  });

  it('maps every day to the Mon→Mon week whose nights contain it, starting on a clicked Monday', () => {
    expect(days.map((d) => periodForClick('WeekMon', d)?.start ?? null)).toEqual(Array(7).fill('2026-10-12'));
    expect(periodForClick('WeekMon', '2026-10-19')).toEqual({ start: '2026-10-19', end: '2026-10-26' });
    expect(periodForClick('WeekMon', '2026-10-18')).toEqual({ start: '2026-10-12', end: '2026-10-19' });
  });

  it('every day is clickable in both Weeks', () => {
    expect(days.every((d) => isClickableDay('WeekFri', d) && isClickableDay('WeekMon', d))).toBe(true);
  });

  it('both Weeks book the Week period type', () => {
    expect([periodTypeOf('WeekFri'), periodTypeOf('WeekMon'), periodTypeOf('Midweek'), periodTypeOf('Weekend')]).toEqual(['Week', 'Week', 'Midweek', 'Weekend']);
  });

  it('applies the overlap rules', () => {
    expect(staysOverlap({ start: '2026-10-09', end: '2026-10-12' }, { start: '2026-10-12', end: '2026-10-16' })).toBe(false);
    expect(stayOverlapsNights({ start: '2026-09-28', end: '2026-10-02' }, { firstNight: '2026-10-02', lastNight: '2026-10-04' })).toBe(false);
    expect(stayOverlapsNights({ start: '2026-12-17', end: '2026-12-21' }, { firstNight: '2026-12-20', lastNight: '2027-01-05' })).toBe(true);
  });
});
