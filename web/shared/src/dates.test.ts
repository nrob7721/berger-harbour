import { describe, expect, it } from 'vitest';
import { formatDate, formatTime, formatTimestamp, sydneyZoneLabel } from './dates';

describe('Australian display formats', () => {
  it('dates are day first', () => {
    expect(formatDate('2026-10-23')).toBe('23/10/2026');
  });

  it('times are 11am / 11:30am', () => {
    expect(formatTime('11:00')).toBe('11am');
    expect(formatTime('08:00')).toBe('8am');
    expect(formatTime('14:00')).toBe('2pm');
    expect(formatTime('11:30')).toBe('11:30am');
    expect(formatTime('00:15')).toBe('12:15am');
    expect(formatTime('12:00')).toBe('12pm');
  });

  it('timestamps are dd/MM/yyyy h:mma in Sydney', () => {
    expect(formatTimestamp('2026-10-12T04:05:00Z')).toBe('12/10/2026 3:05pm'); // AEDT, UTC+11
    expect(formatTimestamp('2027-07-01T14:30:00Z')).toBe('02/07/2027 12:30am'); // AEST, UTC+10
  });
});

describe('Sydney time zone label', () => {
  it.each([
    ['2026-12-07', 'AEDT'],
    ['2027-07-07', 'AEST'],
    ['2026-10-03', 'AEST'], // daylight saving starts Sunday 04/10/2026
    ['2026-10-04', 'AEDT'],
    ['2027-04-04', 'AEST'], // and ends Sunday 04/04/2027
  ])('%s is %s', (date, label) => {
    expect(sydneyZoneLabel(date)).toBe(label);
  });
});
