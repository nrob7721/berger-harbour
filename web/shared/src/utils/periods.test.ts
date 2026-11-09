import { describe, expect, it } from 'vitest';
import { periodForClick, stayOverlapsNights, staysOverlap } from './periods';

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

  it('only Mondays and Fridays start a week', () => {
    expect(days.map((d) => periodForClick('Week', d)?.end ?? null)).toEqual([
      '2026-10-19', null, null, null, '2026-10-23', null, null,
    ]);
  });

  it('applies the overlap rules', () => {
    expect(staysOverlap({ start: '2026-10-09', end: '2026-10-12' }, { start: '2026-10-12', end: '2026-10-16' })).toBe(false);
    expect(stayOverlapsNights({ start: '2026-09-28', end: '2026-10-02' }, { firstNight: '2026-10-02', lastNight: '2026-10-04' })).toBe(false);
    expect(stayOverlapsNights({ start: '2026-12-17', end: '2026-12-21' }, { firstNight: '2026-12-20', lastNight: '2027-01-05' })).toBe(true);
  });
});
