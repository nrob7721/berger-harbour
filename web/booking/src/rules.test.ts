import { describe, expect, it } from 'vitest';
import { adultOptions, capacityView, checkInOutText, childOptions, clampChildren, needsRoomingWarning } from './rules';

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
  it('shows the window with AEDT in summer and AEST in winter', () => {
    expect(checkInOutText(times, '2026-12-07', '2026-12-11')).toBe('Check-in 11am – 2pm · Check-out by 8am (AEDT)');
    expect(checkInOutText(times, '2027-07-05')).toBe('Check-in 11am – 2pm · Check-out by 8am (AEST)');
  });

  it('labels each time when daylight saving starts during the stay', () => {
    // Daylight saving starts Sunday 04/10/2026.
    expect(checkInOutText(times, '2026-10-02', '2026-10-05')).toBe('Check-in 11am – 2pm AEST · Check-out by 8am AEDT');
  });
});
