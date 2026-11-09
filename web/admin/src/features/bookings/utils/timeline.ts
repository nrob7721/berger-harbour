import { addDays, addMonths, daysBetween, formatDateWithWeekday, mondayOf, monthTitle, startOfMonth, type IsoDate } from '@shared/utils/dates';

export type TimelineView = 'month' | 'week';

export interface TimelineRange {
  view: TimelineView;
  /** First day shown. */
  from: IsoDate;
  /** Day after the last day shown. */
  to: IsoDate;
  days: IsoDate[];
  title: string;
}

export function rangeFor(view: TimelineView, anchor: IsoDate): TimelineRange {
  if (view === 'month') {
    const from = startOfMonth(anchor);
    const to = addMonths(from, 1);
    return { view, from, to, days: dayList(from, to), title: monthTitle(from) };
  }
  const from = mondayOf(anchor);
  const to = addDays(from, 7);
  return { view, from, to, days: dayList(from, to), title: `${formatDateWithWeekday(from)} – ${formatDateWithWeekday(addDays(from, 6))}` };
}

function dayList(from: IsoDate, to: IsoDate): IsoDate[] {
  return Array.from({ length: daysBetween(from, to) }, (_, i) => addDays(from, i));
}

/** Prev/Next move one month or one week. */
export function step(range: TimelineRange, delta: 1 | -1): TimelineRange {
  return rangeFor(range.view, range.view === 'month' ? addMonths(range.from, delta) : addDays(range.from, 7 * delta));
}

export function today(view: TimelineView, todayIso: IsoDate): TimelineRange {
  return rangeFor(view, todayIso);
}

/**
 * Month → Week: the week containing today if today is in the displayed month, otherwise the week containing the 1st.
 * Week → Month: the month containing the displayed week's Monday.
 */
export function switchView(range: TimelineRange, view: TimelineView, todayIso: IsoDate): TimelineRange {
  if (view === range.view) return range;
  if (view === 'week') return rangeFor('week', todayIso >= range.from && todayIso < range.to ? todayIso : range.from);
  return rangeFor('month', range.from);
}

const MINUTES_PER_DAY = 1440;

export function minutesOf(hhmm: string): number {
  const [h, m] = hhmm.split(':').map(Number);
  return h * 60 + m;
}

/** Minutes from the range start to a date and time of day. */
function offset(range: Pick<TimelineRange, 'from'>, date: IsoDate, hhmm: string): number {
  return daysBetween(range.from, date) * MINUTES_PER_DAY + minutesOf(hhmm);
}

export interface BarPosition {
  /** Percent of the track from the left. */
  left: number;
  width: number;
  clippedStart: boolean;
  clippedEnd: boolean;
}

/**
 * Each day cell spans 24 hours: a stay starts at its check-in time on the check-in day and ends at its check-out time on the
 * check-out day, linearly within the cells. Null when it is outside the range.
 */
export function barPosition(range: Pick<TimelineRange, 'from' | 'days'>, start: IsoDate, checkIn: string, end: IsoDate, checkOut: string): BarPosition | null {
  const total = range.days.length * MINUTES_PER_DAY;
  const a = offset(range, start, checkIn);
  const b = offset(range, end, checkOut);
  if (b <= 0 || a >= total || b <= a) return null;
  const s = Math.max(0, a);
  const e = Math.min(total, b);
  return { left: (s / total) * 100, width: ((e - s) / total) * 100, clippedStart: a < 0, clippedEnd: b > total };
}

/** Unavailabilities line up with booking bars: from 11am on the first night to 8am the day after the last night. */
export function unavailabilityPosition(range: Pick<TimelineRange, 'from' | 'days'>, firstNight: IsoDate, lastNight: IsoDate): BarPosition | null {
  return barPosition(range, firstNight, '11:00', addDays(lastNight, 1), '08:00');
}

/** Whole-day band for a blocked period. */
export function dayBand(range: Pick<TimelineRange, 'from' | 'days'>, firstNight: IsoDate, lastNight: IsoDate): BarPosition | null {
  return barPosition(range, firstNight, '00:00', addDays(lastNight, 1), '00:00');
}

export interface Timed {
  id: string;
  startDate: IsoDate;
  endDate: IsoDate;
  checkInTime: string;
  checkOutTime: string;
}

/**
 * Stacks bars into lanes by time, not by date: two bars share a lane only if one's check-out is at or before the other's
 * check-in. A standard same-day changeover (8am out, 11am in) stays in one lane; clashing custom times get a new lane.
 */
export function assignLanes<T extends Timed>(items: T[]): { lanes: Map<string, number>; count: number } {
  const instant = (date: IsoDate, hhmm: string) => daysBetween('2000-01-01', date) * MINUTES_PER_DAY + minutesOf(hhmm);
  const sorted = [...items].sort((x, y) => instant(x.startDate, x.checkInTime) - instant(y.startDate, y.checkInTime) || x.id.localeCompare(y.id));
  const laneEnds: number[] = [];
  const lanes = new Map<string, number>();
  for (const item of sorted) {
    const start = instant(item.startDate, item.checkInTime);
    let lane = laneEnds.findIndex((end) => end <= start);
    if (lane < 0) {
      lane = laneEnds.length;
      laneEnds.push(0);
    }
    laneEnds[lane] = instant(item.endDate, item.checkOutTime);
    lanes.set(item.id, lane);
  }
  return { lanes, count: Math.max(1, laneEnds.length) };
}

export const MIN_COLUMN_PX = 28;
