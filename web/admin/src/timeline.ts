import { daysBetween, type IsoDate } from '@shared/dates';

/** Horizontal geometry of a bar in a month view, in units of day columns. */
export interface Span {
  left: number;
  width: number;
  clippedStart: boolean;
  clippedEnd: boolean;
}

/**
 * Bookings start at the middle of the check-in cell and end at the middle of the check-out cell, so a same-day
 * changeover shows both bars in one cell. Returns null when the range is outside the view.
 */
export function bookingSpan(viewFrom: IsoDate, days: number, start: IsoDate, end: IsoDate): Span | null {
  const s = daysBetween(viewFrom, start) + 0.5;
  const e = daysBetween(viewFrom, end) + 0.5;
  if (e <= 0 || s >= days) return null;
  const left = Math.max(0, s);
  const right = Math.min(days, e);
  return { left, width: right - left, clippedStart: s < 0, clippedEnd: e > days };
}

/** Inclusive nights [first, last] drawn like a booking from first to last + 1. */
export function nightsSpan(viewFrom: IsoDate, days: number, firstNight: IsoDate, lastNight: IsoDate): Span | null {
  const s = daysBetween(viewFrom, firstNight) + 0.5;
  const e = daysBetween(viewFrom, lastNight) + 1.5;
  if (e <= 0 || s >= days) return null;
  const left = Math.max(0, s);
  return { left, width: Math.min(days, e) - left, clippedStart: s < 0, clippedEnd: e > days };
}

/** Greedy lane assignment so overlapping bars in one boat row do not cover each other. */
export function assignLanes<T extends { start: IsoDate; end: IsoDate }>(items: T[]): { item: T; lane: number }[] {
  const laneEnds: IsoDate[] = [];
  return [...items]
    .sort((a, b) => (a.start === b.start ? a.end.localeCompare(b.end) : a.start.localeCompare(b.start)))
    .map((item) => {
      let lane = laneEnds.findIndex((end) => end <= item.start);
      if (lane === -1) {
        lane = laneEnds.length;
        laneEnds.push(item.end);
      } else {
        laneEnds[lane] = item.end;
      }
      return { item, lane };
    });
}
