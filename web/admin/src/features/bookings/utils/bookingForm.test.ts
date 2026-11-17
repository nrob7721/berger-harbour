import { describe, expect, it } from 'vitest';
import { defaultEndDate, newBookingPeriod, parseTime24, paymentDateError } from './bookingForm';

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

describe('parseTime24', () => {
  it.each([['09:30', '09:30'], ['9:30', '09:30'], ['1730', '17:30'], ['930', '09:30'], ['0000', '00:00'], ['23:59', '23:59'], [' 8:05 ', '08:05']])(
    'reads %s as %s', (typed, time) => expect(parseTime24(typed)).toBe(time));
  it.each(['24:00', '12:60', '9', '12345', '9.30', 'noon', '', '1:5'])('rejects %s', (typed) => expect(parseTime24(typed)).toBeNull());
});

describe('paymentDateError', () => {
  it('accepts the creation date and today, and rejects the day before creation and tomorrow', () => {
    expect(paymentDateError('2026-08-01', '2026-08-01', '2026-11-17')).toBeNull();
    expect(paymentDateError('2026-11-17', '2026-08-01', '2026-11-17')).toBeNull();
    expect(paymentDateError('2026-07-31', '2026-08-01', '2026-11-17')).toBe("The payment date can't be before the booking was created (01/08/2026).");
    expect(paymentDateError('2026-11-18', '2026-08-01', '2026-11-17')).toBe("The payment date can't be in the future.");
  });
});
