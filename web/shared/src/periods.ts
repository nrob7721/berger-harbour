import { addDays, dayOfWeek, type IsoDate } from './dates';

// Mirrors BergerHarbour.Domain.Bookings.StayPeriodFactory. A stay is the half-open range [start, end):
// check-in on start (1:00pm), check-out on end (8:00am).

export type OnlinePeriodType = 'Midweek' | 'Weekend' | 'Week';

export interface Stay {
  start: IsoDate;
  end: IsoDate;
}

export interface DateRange {
  start: IsoDate;
  end: IsoDate;
}

export interface EnquireOnlyRange {
  name: string;
  firstNight: IsoDate;
  lastNight: IsoDate;
}

export interface AvailabilityData {
  unavailable: DateRange[];
  enquireOnly: EnquireOnlyRange[];
}

export interface BookingWindow {
  /** today + minimum lead time */
  earliestStart: IsoDate;
  /** the last night bookable online */
  bookingsOpenUntil: IsoDate;
}

const MON = 1;
const FRI = 5;
const SAT = 6;
const SUN = 0;

/** The stay a click maps to in the selected mode, or null when the day is not clickable in that mode. */
export function stayForClick(type: OnlinePeriodType, clicked: IsoDate): Stay | null {
  const dow = dayOfWeek(clicked);
  switch (type) {
    case 'Midweek': {
      if (dow < MON || dow > FRI) return null;
      const start = addDays(clicked, -(dow - MON));
      return { start, end: addDays(start, 4) };
    }
    case 'Weekend': {
      const back = dow === FRI ? 0 : dow === SAT ? 1 : dow === SUN ? 2 : dow === MON ? 3 : -1;
      if (back < 0) return null;
      const start = addDays(clicked, -back);
      return { start, end: addDays(start, 3) };
    }
    case 'Week':
      return dow === MON || dow === FRI ? { start: clicked, end: addDays(clicked, 7) } : null;
  }
}

/** Booking vs booking: a.start < b.end && b.start < a.end (same-day changeover does not overlap). */
export function staysOverlap(a: DateRange, b: DateRange): boolean {
  return a.start < b.end && b.start < a.end;
}

/** Booking vs inclusive nights [F, L]: start <= L && F < end. */
export function overlapsNights(stay: Stay, firstNight: IsoDate, lastNight: IsoDate): boolean {
  return stay.start <= lastNight && firstNight < stay.end;
}

export function lastNight(stay: Stay): IsoDate {
  return addDays(stay.end, -1);
}

export type DayStatus =
  | 'not-in-mode' // the day maps to no period in this mode
  | 'too-soon' // the period starts before today + lead time
  | 'after-open-until' // the period has a night after "bookings open until"
  | 'enquire' // the period overlaps a blocked period: clicking shows the phone/email pop-up
  | 'taken' // the period overlaps a booking or unavailability
  | 'available';

export interface DayState {
  status: DayStatus;
  /** The period a click on this day selects (null when not in mode). */
  stay: Stay | null;
  /** Blocked period name when status is 'enquire'. */
  blockedName: string | null;
  /** The day itself is a night inside a blocked period (distinct grey style). */
  insideBlockedPeriod: boolean;
  clickable: boolean;
}

/**
 * Greying is per period, not per day: a day is disabled when the period it maps to is unavailable or invalid. For
 * example a Monday on which a weekend booking checks out is still clickable in Mid-week mode.
 */
export function evaluateDay(type: OnlinePeriodType, day: IsoDate, availability: AvailabilityData, window: BookingWindow): DayState {
  const insideBlockedPeriod = availability.enquireOnly.some((b) => day >= b.firstNight && day <= b.lastNight);
  const stay = stayForClick(type, day);
  const state = (status: DayStatus, blockedName: string | null = null): DayState => ({
    status,
    stay,
    blockedName,
    insideBlockedPeriod,
    clickable: status === 'available' || status === 'enquire',
  });

  if (!stay) return state('not-in-mode');
  if (stay.start < window.earliestStart) return state('too-soon');
  if (lastNight(stay) > window.bookingsOpenUntil) return state('after-open-until');
  const blocked = availability.enquireOnly.find((b) => overlapsNights(stay, b.firstNight, b.lastNight));
  if (blocked) return state('enquire', blocked.name);
  if (availability.unavailable.some((r) => staysOverlap(stay, r))) return state('taken');
  return state('available');
}

/** True when the day is part of the selected stay (check-in through check-out day). */
export function isInSelection(day: IsoDate, selection: Stay | null): boolean {
  return !!selection && day >= selection.start && day <= selection.end;
}
