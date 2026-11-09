import { describe, expect, it } from 'vitest';
import { formatDate, formatDateWithWeekday, formatTime, monthGrid, zoneLabel, formatTimestamp } from './dates';

describe('dates', () => {
  it('formats Australian dates and times', () => {
    expect(formatDate('2026-10-16')).toBe('16/10/2026');
    expect(formatDateWithWeekday('2026-10-16')).toBe('Fri 16/10/2026');
    expect(formatTime('11:00')).toBe('11am');
    expect(formatTime('11:30')).toBe('11:30am');
    expect(formatTime('08:00')).toBe('8am');
    expect(formatTime('23:00')).toBe('11pm');
    expect(formatTimestamp('2027-01-15T00:30:00Z')).toBe('15/01/2027 11:30am');
  });

  it('labels the Sydney time zone for the date', () => {
    expect(zoneLabel('2027-01-15')).toBe('AEDT/NSW');
    expect(zoneLabel('2027-07-15')).toBe('AEST/NSW');
    expect(zoneLabel('2027-04-03')).toBe('AEDT/NSW'); // DST ends Sun 04/04/2027
    expect(zoneLabel('2027-04-05')).toBe('AEST/NSW');
  });

  it('builds Monday-first month grids with only the weeks of the month', () => {
    const march2027 = monthGrid('2027-03-01'); // starts on a Monday
    expect(march2027[0][0]).toBe('2027-03-01');
    expect(march2027).toHaveLength(5);
    const feb2027 = monthGrid('2027-02-10');
    expect(feb2027[0][0]).toBe('2027-02-01');
    const oct2026 = monthGrid('2026-10-01');
    expect(oct2026[0][0]).toBe('2026-09-28');
    expect(oct2026[oct2026.length - 1][6]).toBe('2026-11-01');
  });
});
