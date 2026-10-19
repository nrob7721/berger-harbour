import { describe, expect, it } from 'vitest';
import {
  adultOptions, capacityView, checkInOutText, childOptions, clampChildren, needsRoomingWarning, selectionText, summaryTimes,
} from './rules';

const times = { checkInTime: '11am', checkInWindowEnd: '2pm', checkOutTime: '8am' };

describe('guests', () => {
  it('offers 1 … max adults and 0 … (max − adults) children', () => {
    expect(adultOptions(4)).toEqual([1, 2, 3, 4]);
    expect(childOptions(12, 9)).toEqual([0, 1, 2, 3]);
    expect(childOptions(4, 4)).toEqual([0]);
  });

  it('clamps children when adults change so the total never exceeds max guests', () => {
    expect(clampChildren(12, 10, 4)).toBe(2);
    expect(clampChildren(12, 4, 4)).toBe(4);
    expect(clampChildren(12, 12, 1)).toBe(0);
  });

  it('warns about rooming when adults + children exceed adult beds + kids beds', () => {
    expect(needsRoomingWarning(6, 2, 6, 2)).toBe(false);
    expect(needsRoomingWarning(7, 2, 6, 2)).toBe(true);
    expect(needsRoomingWarning(4, 1, 4, 0)).toBe(true);
    expect(needsRoomingWarning(4, 1, null, null)).toBe(false);
  });
});

describe('capacity-limited add-ons', () => {
  const parking = { hasCapacity: true, capacityUnitLabel: 'car spots' };

  it('shows what is left and limits the quantity', () => {
    expect(capacityView(parking, 6)).toEqual({ hidden: false, text: '6 car spots remaining', max: 6 });
    expect(capacityView({ hasCapacity: true, capacityUnitLabel: null }, 2).text).toBe('2 available for your dates');
  });

  it('hides the add-on when nothing is left', () => {
    expect(capacityView(parking, 0).hidden).toBe(true);
  });

  it('leaves ordinary add-ons and unknown figures alone', () => {
    expect(capacityView({ hasCapacity: false, capacityUnitLabel: null }, undefined)).toEqual({ hidden: false, text: null, max: 99 });
    expect(capacityView(parking, undefined)).toEqual({ hidden: false, text: null, max: 99 });
  });
});

describe('check-in and check-out text', () => {
  it('shows the window with AEDT/NSW in summer and AEST/NSW in winter', () => {
    expect(checkInOutText(times, '2026-12-07', '2026-12-11')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
    expect(checkInOutText(times, '2027-07-05')).toBe('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
  });

  it('labels each time when daylight saving changes during the stay', () => {
    // Daylight saving starts Sunday 04/10/2026 and ends Sunday 04/04/2027.
    expect(checkInOutText(times, '2026-10-02', '2026-10-05'))
      .toBe('Check-in 11am – 2pm (AEST/NSW) · Check-out from 8am (AEDT/NSW)');
    expect(checkInOutText(times, '2027-04-02', '2027-04-05'))
      .toBe('Check-in 11am – 2pm (AEDT/NSW) · Check-out from 8am (AEST/NSW)');
  });
});

describe('selected dates text', () => {
  it('states the dates without repeating the times', () => {
    expect(selectionText('2026-10-16', '2026-10-23')).toBe('Selected: check-in Fri 16/10/2026, check-out Fri 23/10/2026.');
  });
});

describe('summary check-in and check-out rows', () => {
  it('shows each date with its time and zone', () => {
    expect(summaryTimes(times, '2026-10-16', '2026-10-23')).toEqual({
      checkIn: 'Fri 16/10/2026, 11am – 2pm AEDT/NSW',
      checkOut: 'Fri 23/10/2026, from 8am AEDT/NSW',
    });
  });

  it('labels each row with its own zone across a daylight-saving change', () => {
    expect(summaryTimes(times, '2026-10-02', '2026-10-05')).toEqual({
      checkIn: 'Fri 02/10/2026, 11am – 2pm AEST/NSW',
      checkOut: 'Mon 05/10/2026, from 8am AEDT/NSW',
    });
  });
});
