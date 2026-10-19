import { addDays, addMonths, dayOfWeek, daysBetween, firstOfMonth, formatLongDate, monthTitle, type IsoDate } from '@shared/dates';

/** Standard times ("HH:mm"); unavailabilities are drawn with them so they line up with booking bars. */
export const STANDARD_CHECK_IN = '11:00';
export const STANDARD_CHECK_OUT = '08:00';

// ---- Week | Month view ---------------------------------------------------------------------------------------------

export type TimelineMode = 'Month' | 'Week';

/** What the timeline shows: a month (anchor is its 1st) or a Mon → Sun week (anchor is its Monday). */
export interface TimelineView {
  mode: TimelineMode;
  anchor: IsoDate;
}

/** The Monday of the week containing the day. */
export function mondayOf(day: IsoDate): IsoDate {
  return addDays(day, -((dayOfWeek(day) + 6) % 7));
}

/** The view that Today shows: the current month, or the week containing today. */
export function todayView(mode: TimelineMode, today: IsoDate): TimelineView {
  return { mode, anchor: mode === 'Month' ? firstOfMonth(today) : mondayOf(today) };
}

/** Prev/Next: one month, or one week. */
export function stepView(view: TimelineView, direction: 1 | -1): TimelineView {
  return { mode: view.mode, anchor: view.mode === 'Month' ? addMonths(view.anchor, direction) : addDays(view.anchor, 7 * direction) };
}

/**
 * Month → Week shows the week containing today when today is in the displayed month, otherwise the week containing
 * the 1st. Week → Month shows the month containing the week's Monday.
 */
export function switchView(view: TimelineView, mode: TimelineMode, today: IsoDate): TimelineView {
  if (mode === view.mode) return view;
  if (mode === 'Month') return { mode, anchor: firstOfMonth(view.anchor) };
  return { mode, anchor: mondayOf(firstOfMonth(today) === view.anchor ? today : view.anchor) };
}

/** The days shown: [from, to). */
export function viewRange(view: TimelineView): { from: IsoDate; to: IsoDate; days: number } {
  const to = view.mode === 'Month' ? addMonths(view.anchor, 1) : addDays(view.anchor, 7);
  return { from: view.anchor, to, days: daysBetween(view.anchor, to) };
}

/** "October 2026", or "Mon 19/10/2026 – Sun 25/10/2026". */
export function viewTitle(view: TimelineView): string {
  return view.mode === 'Month' ? monthTitle(view.anchor) : `${formatLongDate(view.anchor)} – ${formatLongDate(addDays(view.anchor, 6))}`;
}

// ---- Geometry ------------------------------------------------------------------------------------------------------

/** Horizontal geometry of a bar, in units of day columns. */
export interface Span {
  left: number;
  width: number;
  clippedStart: boolean;
  clippedEnd: boolean;
}

/** "HH:mm" (seconds ignored) as a fraction of the day: 11:00 → 0.458…, 08:00 → 0.333…. */
export function dayFraction(hhmm: string): number {
  const [hours = 0, minutes = 0] = hhmm.split(':').map(Number);
  return (hours * 60 + minutes) / (24 * 60);
}

function clip(s: number, e: number, days: number): Span | null {
  if (e <= 0 || s >= days) return null;
  const left = Math.max(0, s);
  return { left, width: Math.min(days, e) - left, clippedStart: s < 0, clippedEnd: e > days };
}

/**
 * Each day cell spans 24 hours: a booking starts at its check-in time on the check-in day and ends at its check-out
 * time on the check-out day, so a standard changeover (8am out, 11am in) shows both bars in one cell. Returns null when
 * the range is outside the view.
 */
export function bookingSpan(viewFrom: IsoDate, days: number, start: IsoDate, end: IsoDate,
  checkInTime: string = STANDARD_CHECK_IN, checkOutTime: string = STANDARD_CHECK_OUT): Span | null {
  return clip(daysBetween(viewFrom, start) + dayFraction(checkInTime), daysBetween(viewFrom, end) + dayFraction(checkOutTime), days);
}

/** An unavailability's inclusive nights, drawn with the standard times: 11am on the first night to 8am after the last. */
export function unavailabilitySpan(viewFrom: IsoDate, days: number, firstNight: IsoDate, lastNight: IsoDate): Span | null {
  return bookingSpan(viewFrom, days, firstNight, addDays(lastNight, 1));
}

/** A blocked period covers the whole days of its inclusive nights. */
export function blockedSpan(viewFrom: IsoDate, days: number, firstNight: IsoDate, lastNight: IsoDate): Span | null {
  return clip(daysBetween(viewFrom, firstNight), daysBetween(viewFrom, lastNight) + 1, days);
}

// ---- Lanes ---------------------------------------------------------------------------------------------------------

/** A sortable local date and time, "yyyy-MM-ddTHH:mm". */
export function stamp(date: IsoDate, hhmm: string): string {
  return `${date}T${hhmm.slice(0, 5)}`;
}

/**
 * Greedy lane assignment by time, so bars in one boat row never cover each other. Two bars share a lane only if one
 * ends (check-out) at or before the other starts (check-in): a standard changeover shares a lane, clashing custom times
 * do not. `start` and `end` are stamps.
 */
export function assignLanes<T extends { start: string; end: string }>(items: T[]): { item: T; lane: number }[] {
  const laneEnds: string[] = [];
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
