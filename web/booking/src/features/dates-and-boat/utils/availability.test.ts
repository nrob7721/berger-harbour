import { describe, expect, it } from 'vitest';
import { availabilityWindow, checkInOutLine, dayState, requestedBoatReason, selectedLine } from './availability';
import type { FleetAvailability, PeriodAvailability } from '@/lib/types';

const settings = { minimumLeadTimeDays: 30, bookingsOpenUntil: '2027-11-30', today: '2026-08-01', contactPhone: null, contactEmail: null, hireTermsUrl: '' };
const availability: FleetAvailability = {
  enquireOnly: [{ name: 'Christmas', firstNight: '2026-12-20', lastNight: '2027-01-05' }],
  boats: [
    { slug: 'a', unavailable: [{ start: '2026-10-09', end: '2026-10-12' }] },
    { slug: 'b', unavailable: [] },
  ],
};

describe('per-period greying', () => {
  it('greys a day when the period it maps to is unavailable for every calendar boat', () => {
    expect(dayState('2026-10-10', true, 'Weekend', availability, ['a'], settings).kind).toBe('disabled');
    expect(dayState('2026-10-10', true, 'Weekend', availability, ['a', 'b'], settings).kind).toBe('available');
  });

  it('keeps a Monday clickable in mid-week mode even when a weekend booking checks out that day', () => {
    expect(dayState('2026-10-12', true, 'Midweek', availability, ['a'], settings).kind).toBe('available');
  });

  it('applies lead time and bookings-open-until to whole periods', () => {
    expect(dayState('2026-08-28', true, 'Weekend', availability, ['b'], settings).kind).toBe('disabled'); // starts 28/08 < 31/08
    expect(dayState('2026-09-04', true, 'Weekend', availability, ['b'], settings).kind).toBe('available');
    expect(dayState('2027-11-29', true, 'Midweek', availability, ['b'], settings).kind).toBe('disabled'); // nights run past 30/11/2027
  });

  it('maps clicks on periods overlapping a blocked period to the enquiry pop-up', () => {
    expect(dayState('2026-12-15', true, 'Midweek', availability, ['b'], settings).kind).toBe('available'); // 14/12 → 18/12
    expect(dayState('2026-12-18', true, 'WeekFri', availability, ['b'], settings).kind).toBe('enquire');
    expect(dayState('2026-12-16', true, 'Midweek', availability, ['b'], settings)).toEqual({ kind: 'available', period: { start: '2026-12-14', end: '2026-12-18' } });
  });

  it('makes every day clickable in both Weeks, mapping it to the week whose nights contain it', () => {
    expect(dayState('2026-10-15', true, 'WeekFri', availability, ['b'], settings)).toEqual({ kind: 'available', period: { start: '2026-10-09', end: '2026-10-16' } });
    expect(dayState('2026-10-17', true, 'WeekMon', availability, ['b'], settings)).toEqual({ kind: 'available', period: { start: '2026-10-12', end: '2026-10-19' } });
  });

  it('covers a week either side of the grid, so Week clicks on its first days are known', () => {
    const grid = [['2026-09-28', '2026-09-29', '2026-09-30', '2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04'],
      ['2026-10-26', '2026-10-27', '2026-10-28', '2026-10-29', '2026-10-30', '2026-10-31', '2026-11-01']];
    expect(availabilityWindow(grid)).toEqual({ from: '2026-09-21', to: '2026-11-09' });
  });

  it('never makes adjacent-month cells clickable', () => {
    expect(dayState('2026-10-01', false, 'Midweek', availability, ['b'], settings).kind).toBe('adjacent');
  });
});

describe('check-in line and selected text', () => {
  it('labels the zone of the check-out date, also for a stay that crosses a daylight-saving change', () => {
    expect(checkInOutLine({ start: '2027-01-15', end: '2027-01-18' }, '2027-01-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
    expect(checkInOutLine({ start: '2027-07-05', end: '2027-07-09' }, '2027-07-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    // Daylight saving ends on Sun 04/04/2027: one label at the end, the check-out date's.
    expect(checkInOutLine({ start: '2027-04-02', end: '2027-04-09' }, '2027-04-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    expect(checkInOutLine({ start: '2026-03-27', end: '2026-04-03' }, '2026-03-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
    expect(checkInOutLine({ start: '2026-10-02', end: '2026-10-09' }, '2026-10-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
  });

  it('uses the visible month when nothing is selected', () => {
    expect(checkInOutLine(null, '2026-08-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    expect(checkInOutLine(null, '2026-12-01')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
  });

  it('states the selection with weekdays and dates but no times', () => {
    expect(selectedLine({ start: '2026-10-16', end: '2026-10-23' })).toBe('Selected: check-in Fri 16/10/2026, check-out Fri 23/10/2026.');
  });
});

describe("requested boat's reason", () => {
  const boat = { slug: 'a', name: 'A', imageUrl: null, berth: 8, hasSpa: false, isPetFriendly: false, isActive: true };
  const f = { adults: 2, children: 0, spa: false, pets: false };
  const unavailable: PeriodAvailability = { periodType: 'Midweek', startDate: '2026-10-12', endDate: '2026-10-16', boats: [{ slug: 'a', available: false, hirePrice: 1 }] };

  it('uses the first reason that applies: inactive → dates → too small → filters', () => {
    const everything = { ...f, adults: 10, spa: true };
    expect(requestedBoatReason({ ...boat, isActive: false }, everything, unavailable)).toBe('Not available for online booking');
    expect(requestedBoatReason(boat, everything, unavailable)).toBe('Not available for selected dates');
    expect(requestedBoatReason(boat, everything, undefined)).toBe('Too small for 10 guests');
    expect(requestedBoatReason(boat, { ...f, spa: true }, undefined)).toBe("Doesn't match your filters");
    expect(requestedBoatReason(boat, f, undefined)).toBeNull();
  });
});
