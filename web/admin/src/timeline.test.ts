import { describe, expect, it } from 'vitest';
import {
  assignLanes, blockedSpan, bookingSpan, dayFraction, mondayOf, stamp, stepView, switchView, todayView, unavailabilitySpan,
  viewRange, viewTitle,
} from './timeline';

describe('week and month views', () => {
  it('weeks run Mon → Sun', () => {
    expect(mondayOf('2026-10-19')).toBe('2026-10-19'); // Monday
    expect(mondayOf('2026-10-25')).toBe('2026-10-19'); // Sunday
    expect(viewRange({ mode: 'Week', anchor: '2026-10-19' })).toEqual({ from: '2026-10-19', to: '2026-10-26', days: 7 });
    expect(viewTitle({ mode: 'Week', anchor: '2026-10-19' })).toBe('Mon 19/10/2026 – Sun 25/10/2026');
  });

  it('months show every day of the month', () => {
    expect(viewRange({ mode: 'Month', anchor: '2026-10-01' })).toEqual({ from: '2026-10-01', to: '2026-11-01', days: 31 });
    expect(viewTitle({ mode: 'Month', anchor: '2026-10-01' })).toBe('October 2026');
  });

  it('Prev/Next move one week or one month', () => {
    expect(stepView({ mode: 'Week', anchor: '2026-10-26' }, 1)).toEqual({ mode: 'Week', anchor: '2026-11-02' });
    expect(stepView({ mode: 'Week', anchor: '2026-10-26' }, -1)).toEqual({ mode: 'Week', anchor: '2026-10-19' });
    expect(stepView({ mode: 'Month', anchor: '2026-12-01' }, 1)).toEqual({ mode: 'Month', anchor: '2027-01-01' });
  });

  it('Today goes to the week containing today, or the current month', () => {
    expect(todayView('Week', '2026-10-22')).toEqual({ mode: 'Week', anchor: '2026-10-19' });
    expect(todayView('Month', '2026-10-22')).toEqual({ mode: 'Month', anchor: '2026-10-01' });
  });

  it('Month → Week shows the week containing today when today is in the month, otherwise the week of the 1st', () => {
    expect(switchView({ mode: 'Month', anchor: '2026-10-01' }, 'Week', '2026-10-22')).toEqual({ mode: 'Week', anchor: '2026-10-19' });
    // 01/12/2026 is a Tuesday, so its week starts in November.
    expect(switchView({ mode: 'Month', anchor: '2026-12-01' }, 'Week', '2026-10-22')).toEqual({ mode: 'Week', anchor: '2026-11-30' });
  });

  it('Week → Month shows the month containing the Monday', () => {
    expect(switchView({ mode: 'Week', anchor: '2026-11-30' }, 'Month', '2026-10-22')).toEqual({ mode: 'Month', anchor: '2026-11-01' });
  });
});

describe('timeline geometry', () => {
  it('turns times into a fraction of the day', () => {
    expect(dayFraction('11:00')).toBeCloseTo(0.4583, 4);
    expect(dayFraction('08:00')).toBeCloseTo(0.3333, 4);
    expect(dayFraction('23:30:00')).toBeCloseTo(0.9792, 4);
  });

  it('bars run from the check-in time to the check-out time', () => {
    const span = bookingSpan('2026-11-01', 30, '2026-11-16', '2026-11-20')!;
    expect(span.left).toBeCloseTo(15 + 11 / 24);
    expect(span.left + span.width).toBeCloseTo(19 + 8 / 24);
  });

  it('reflects custom times: an 11pm check-out ends close to the next day', () => {
    const span = bookingSpan('2026-11-01', 30, '2026-11-16', '2026-11-20', '09:30', '23:00')!;
    expect(span.left).toBeCloseTo(15 + 9.5 / 24);
    expect(span.left + span.width).toBeCloseTo(19 + 23 / 24);
  });

  it('positions bars the same way in a week view', () => {
    const span = bookingSpan('2026-10-19', 7, '2026-10-23', '2026-10-26')!; // Fri → Mon
    expect(span.left).toBeCloseTo(4 + 11 / 24);
    expect(span).toMatchObject({ clippedStart: false, clippedEnd: true });
    expect(span.left + span.width).toBe(7);
  });

  it('clips bars that cross the view edges', () => {
    const before = bookingSpan('2026-11-01', 30, '2026-10-30', '2026-11-02')!;
    expect(before).toMatchObject({ left: 0, clippedStart: true, clippedEnd: false });
    expect(before.width).toBeCloseTo(1 + 8 / 24);
    expect(bookingSpan('2026-11-01', 30, '2026-11-29', '2026-12-03')).toMatchObject({ clippedStart: false, clippedEnd: true });
    expect(bookingSpan('2026-11-01', 30, '2026-12-01', '2026-12-03')).toBeNull();
    // Checking out on the first day of the view still shows that morning.
    expect(bookingSpan('2026-11-01', 30, '2026-10-28', '2026-11-01')!.width).toBeCloseTo(8 / 24);
  });

  it('unavailability runs from 11am on the first night to 8am after the last, like a booking', () => {
    const span = unavailabilitySpan('2026-11-01', 30, '2026-11-10', '2026-11-10')!;
    expect(span.left).toBeCloseTo(9 + 11 / 24);
    expect(span.left + span.width).toBeCloseTo(10 + 8 / 24);
  });

  it('blocked periods cover whole days', () => {
    expect(blockedSpan('2026-12-01', 31, '2026-12-20', '2027-01-03')).toEqual({ left: 19, width: 12, clippedStart: false, clippedEnd: true });
    expect(blockedSpan('2026-12-01', 31, '2026-12-24', '2026-12-26')).toEqual({ left: 23, width: 3, clippedStart: false, clippedEnd: false });
  });
});

describe('lanes', () => {
  const booking = (start: string, checkIn: string, end: string, checkOut: string) =>
    ({ start: stamp(start, checkIn), end: stamp(end, checkOut) });

  it('a standard same-day changeover shares a lane; date overlaps do not', () => {
    const lanes = assignLanes([
      booking('2026-11-13', '11:00', '2026-11-16', '08:00'),
      booking('2026-11-16', '11:00', '2026-11-20', '08:00'),
      booking('2026-11-17', '11:00', '2026-11-19', '08:00'),
    ]);
    expect(lanes.map((l) => l.lane)).toEqual([0, 0, 1]);
  });

  it('clashing custom times on a changeover day put the later booking in a new lane', () => {
    const lanes = assignLanes([
      booking('2026-11-13', '11:00', '2026-11-16', '23:00'), // late check-out
      booking('2026-11-16', '11:00', '2026-11-20', '08:00'),
    ]);
    expect(lanes.map((l) => l.lane)).toEqual([0, 1]);
  });

  it('a check-out at the exact check-in time still shares the lane', () => {
    const lanes = assignLanes([
      booking('2026-11-13', '11:00', '2026-11-16', '11:00'),
      booking('2026-11-16', '11:00', '2026-11-20', '08:00'),
    ]);
    expect(lanes.map((l) => l.lane)).toEqual([0, 0]);
  });

  it('accepts API times with seconds', () => {
    expect(stamp('2026-11-16', '08:00:00')).toBe('2026-11-16T08:00');
  });
});
