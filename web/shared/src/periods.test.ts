import { describe, expect, it } from 'vitest';
import { evaluateDay, stayForClick, type AvailabilityData, type BookingWindow } from './periods';
import { sydneyToday } from './dates';

// Week of Mon 12/10/2026 … Sun 18/10/2026.
const d = (day: number, month = 10, year = 2026) => `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;

describe('stayForClick', () => {
  it.each([12, 13, 14, 15, 16])('Mid-week: day %i maps to Mon 12 → Fri 16', (day) => {
    expect(stayForClick('Midweek', d(day))).toEqual({ start: d(12), end: d(16) });
  });

  it.each([17, 18])('Mid-week: weekend day %i is not clickable', (day) => {
    expect(stayForClick('Midweek', d(day))).toBeNull();
  });

  it.each([16, 17, 18, 19])('Weekend: day %i maps to Fri 16 → Mon 19', (day) => {
    expect(stayForClick('Weekend', d(day))).toEqual({ start: d(16), end: d(19) });
  });

  it.each([13, 14, 15])('Weekend: Tue–Thu (%i) are not clickable', (day) => {
    expect(stayForClick('Weekend', d(day))).toBeNull();
  });

  it('Weekend: clicking Monday selects the weekend ending that day', () => {
    expect(stayForClick('Weekend', d(12))).toEqual({ start: d(9), end: d(12) });
  });

  it('Week: Monday starts Mon → Mon and Friday starts Fri → Fri', () => {
    expect(stayForClick('Week', d(12))).toEqual({ start: d(12), end: d(19) });
    expect(stayForClick('Week', d(16))).toEqual({ start: d(16), end: d(23) });
  });

  it.each([13, 14, 15, 17, 18])('Week: day %i is not clickable', (day) => {
    expect(stayForClick('Week', d(day))).toBeNull();
  });

  it('Mid-week across a month boundary', () => {
    expect(stayForClick('Midweek', d(1))).toEqual({ start: d(28, 9), end: d(2) });
  });

  it('Mid-week across a year boundary', () => {
    expect(stayForClick('Midweek', d(1, 1, 2027))).toEqual({ start: d(28, 12), end: d(1, 1, 2027) });
  });
});

describe('evaluateDay', () => {
  const window: BookingWindow = { earliestStart: d(1), bookingsOpenUntil: d(30, 11, 2027) };
  const empty: AvailabilityData = { unavailable: [], enquireOnly: [] };

  it('greys per period: a Monday a weekend checks out on is still clickable in Mid-week mode', () => {
    const availability: AvailabilityData = { unavailable: [{ start: d(16), end: d(19) }], enquireOnly: [] };
    expect(evaluateDay('Midweek', d(19), availability, window).status).toBe('available');
    expect(evaluateDay('Weekend', d(19), availability, window).status).toBe('taken');
    expect(evaluateDay('Midweek', d(16), availability, window).status).toBe('available');
    // In Week mode Friday 16 → Fri 23 overlaps the weekend.
    expect(evaluateDay('Week', d(16), availability, window).status).toBe('taken');
    // Mon 12 → Mon 19 overlaps too.
    expect(evaluateDay('Week', d(12), availability, window).status).toBe('taken');
  });

  it('every day of a taken period is disabled, not only the booked nights', () => {
    const availability: AvailabilityData = { unavailable: [{ start: d(14), end: d(15) }], enquireOnly: [] };
    for (const day of [12, 13, 14, 15, 16]) {
      expect(evaluateDay('Midweek', d(day), availability, window).clickable).toBe(false);
    }
  });

  it('days outside the mode are not clickable', () => {
    const state = evaluateDay('Week', d(14), empty, window);
    expect(state.status).toBe('not-in-mode');
    expect(state.clickable).toBe(false);
  });

  it('lead time boundary is per period start', () => {
    const w: BookingWindow = { earliestStart: d(13), bookingsOpenUntil: d(30, 11, 2027) };
    // Thu 15 maps to a period starting Mon 12, which is before the earliest start.
    expect(evaluateDay('Midweek', d(15), empty, w).status).toBe('too-soon');
    expect(evaluateDay('Weekend', d(16), empty, w).status).toBe('available');
  });

  it('open-until boundary uses the last night', () => {
    const w: BookingWindow = { earliestStart: d(1), bookingsOpenUntil: d(18) };
    // Weekend Fri 16 → Mon 19 has last night Sun 18: inside.
    expect(evaluateDay('Weekend', d(16), empty, w).status).toBe('available');
    // Week Fri 16 → Fri 23 has last night Thu 22: outside.
    expect(evaluateDay('Week', d(16), empty, w).status).toBe('after-open-until');
  });

  it('blocked periods produce the enquire pop-up, and their days are styled', () => {
    // Mid-week Mon 17/12 → Fri 21/12/2029 overlaps Christmas from 20/12.
    const availability: AvailabilityData = {
      unavailable: [],
      enquireOnly: [{ name: 'Christmas / New Year', firstNight: d(20, 12, 2029), lastNight: d(5, 1, 2030) }],
    };
    const w: BookingWindow = { earliestStart: d(1, 1, 2029), bookingsOpenUntil: d(31, 12, 2030) };
    const tuesday = evaluateDay('Midweek', d(18, 12, 2029), availability, w);
    expect(tuesday.status).toBe('enquire');
    expect(tuesday.clickable).toBe(true);
    expect(tuesday.blockedName).toBe('Christmas / New Year');
    expect(tuesday.insideBlockedPeriod).toBe(false);
    expect(evaluateDay('Midweek', d(20, 12, 2029), availability, w).insideBlockedPeriod).toBe(true);
    // The week before is fine.
    expect(evaluateDay('Midweek', d(11, 12, 2029), availability, w).status).toBe('available');
  });

  it('a mid-week ending on the first night of a long weekend is not blocked', () => {
    const availability: AvailabilityData = {
      unavailable: [],
      enquireOnly: [{ name: 'Labour Day long weekend', firstNight: d(2), lastNight: d(4) }],
    };
    const w: BookingWindow = { earliestStart: d(1, 9), bookingsOpenUntil: d(30, 11, 2027) };
    expect(evaluateDay('Midweek', d(29, 9), availability, w).status).toBe('available');
    expect(evaluateDay('Weekend', d(2), availability, w).status).toBe('enquire');
  });
});

describe('sydneyToday', () => {
  it('uses the Sydney date, not the browser date', () => {
    // 2026-10-05T14:30Z is already 6 October in Sydney (AEDT, UTC+11).
    expect(sydneyToday(new Date('2026-10-05T14:30:00Z'))).toBe('2026-10-06');
  });
});
