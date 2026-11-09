import { describe, expect, it } from 'vitest';
import { assignLanes, barPosition, rangeFor, step, switchView, today, unavailabilityPosition } from './timeline';

describe('timeline ranges', () => {
  it('week view runs Monday to Sunday and steps by a week', () => {
    const week = rangeFor('week', '2026-10-21');
    expect(week.from).toBe('2026-10-19');
    expect(week.days).toHaveLength(7);
    expect(week.days[6]).toBe('2026-10-25');
    expect(week.title).toBe('Mon 19/10/2026 – Sun 25/10/2026');
    expect(step(week, 1).from).toBe('2026-10-26');
    expect(step(week, -1).from).toBe('2026-10-12');
    expect(today('week', '2026-10-25').from).toBe('2026-10-19'); // Sunday belongs to the week starting Monday
  });

  it('month view has one column per day and steps by a month', () => {
    const month = rangeFor('month', '2026-10-15');
    expect(month.title).toBe('October 2026');
    expect(month.days).toHaveLength(31);
    expect(step(month, 1).title).toBe('November 2026');
    expect(step(month, -1).days).toHaveLength(30);
  });

  it('switching month → week shows the week of today when today is in the month, else the week of the 1st', () => {
    const october = rangeFor('month', '2026-10-01');
    expect(switchView(october, 'week', '2026-10-21').from).toBe('2026-10-19');
    expect(switchView(october, 'week', '2026-11-09').from).toBe('2026-09-28');
  });

  it('switching week → month shows the month of the week\'s Monday', () => {
    expect(switchView(rangeFor('week', '2026-11-01'), 'month', '2026-11-09').title).toBe('October 2026');
  });
});

describe('bar positioning', () => {
  const week = rangeFor('week', '2026-10-19'); // Mon 19 … Sun 25

  it('places bars by check-in and check-out time within day cells', () => {
    const p = barPosition(week, '2026-10-19', '11:00', '2026-10-23', '08:00')!;
    const day = 100 / 7;
    expect(p.left).toBeCloseTo(day * (11 / 24));
    expect(p.left + p.width).toBeCloseTo(day * (4 + 8 / 24));
  });

  it('reflects custom times', () => {
    const p = barPosition(week, '2026-10-19', '11:00', '2026-10-21', '23:00')!;
    expect(p.left + p.width).toBeCloseTo((100 / 7) * (2 + 23 / 24));
  });

  it('clips bars at the range edges and skips bars outside it', () => {
    const p = barPosition(week, '2026-10-16', '11:00', '2026-10-20', '08:00')!;
    expect(p.left).toBe(0);
    expect(p.clippedStart).toBe(true);
    expect(barPosition(week, '2026-10-26', '11:00', '2026-10-30', '08:00')).toBeNull();
  });

  it('positions unavailabilities with the standard times', () => {
    const p = unavailabilityPosition(week, '2026-10-20', '2026-10-21')!;
    const day = 100 / 7;
    expect(p.left).toBeCloseTo(day * (1 + 11 / 24));
    expect(p.left + p.width).toBeCloseTo(day * (3 + 8 / 24));
  });
});

describe('lanes', () => {
  const b = (id: string, start: string, checkIn: string, end: string, checkOut: string) => ({ id, startDate: start, checkInTime: checkIn, endDate: end, checkOutTime: checkOut });

  it('keeps a standard same-day changeover in one lane', () => {
    const { lanes, count } = assignLanes([b('a', '2026-10-16', '11:00', '2026-10-19', '08:00'), b('b', '2026-10-19', '11:00', '2026-10-23', '08:00')]);
    expect(count).toBe(1);
    expect(lanes.get('a')).toBe(lanes.get('b'));
  });

  it('puts clashing custom times in a new lane', () => {
    const { lanes, count } = assignLanes([b('a', '2026-10-16', '11:00', '2026-10-19', '23:00'), b('b', '2026-10-19', '11:00', '2026-10-23', '08:00')]);
    expect(count).toBe(2);
    expect(lanes.get('b')).toBe(1);
  });

  it('reuses freed lanes', () => {
    const { count } = assignLanes([
      b('a', '2026-10-01', '11:00', '2026-10-05', '08:00'),
      b('b', '2026-10-03', '11:00', '2026-10-06', '08:00'),
      b('c', '2026-10-05', '11:00', '2026-10-08', '08:00'),
    ]);
    expect(count).toBe(2);
  });
});
