import { describe, expect, it } from 'vitest';
import { defaultEndDate, newBookingPeriod } from './bookingForm';

describe('new booking pre-fill', () => {
  // Week of Mon 12/10/2026 – Sun 18/10/2026.
  it.each([
    ['2026-10-12', 'Midweek', '2026-10-12', '2026-10-16'], // Mon
    ['2026-10-13', 'Midweek', '2026-10-12', '2026-10-16'], // Tue
    ['2026-10-14', 'Midweek', '2026-10-12', '2026-10-16'], // Wed
    ['2026-10-15', 'Midweek', '2026-10-12', '2026-10-16'], // Thu: starts before the day clicked
    ['2026-10-16', 'Weekend', '2026-10-16', '2026-10-19'], // Fri
    ['2026-10-17', 'Weekend', '2026-10-16', '2026-10-19'], // Sat
    ['2026-10-18', 'Weekend', '2026-10-16', '2026-10-19'], // Sun
  ])('%s → %s %s → %s', (day, periodType, startDate, endDate) => {
    expect(newBookingPeriod(day)).toEqual({ periodType, startDate, endDate });
  });

  it('changing the start date keeps the period type and re-derives the end date', () => {
    expect(defaultEndDate('Midweek', '2026-10-19', '2026-10-16')).toBe('2026-10-23');
    expect(defaultEndDate('Weekend', '2026-10-23', '2026-10-19')).toBe('2026-10-26');
    expect(defaultEndDate('Week', '2026-10-12', '2026-10-16')).toBe('2026-10-19');
  });
});
