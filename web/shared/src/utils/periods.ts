import { addDays, dayOfWeek, type IsoDate } from './dates';

export type OnlinePeriodType = 'Midweek' | 'Weekend' | 'Week';

/**
 * The booking app's choice of period (§3 Type of stay). Both Week options are the `Week` period type; they only set the weekday
 * the week starts on.
 */
export type TypeOfStay = 'Midweek' | 'Weekend' | 'WeekFri' | 'WeekMon';

export const TYPES_OF_STAY: readonly TypeOfStay[] = ['Midweek', 'Weekend', 'WeekFri', 'WeekMon'];

export const TYPE_OF_STAY_LABELS: Record<TypeOfStay, { name: string; days: string }> = {
  Midweek: { name: 'Mid-week', days: 'Mon–Fri' },
  Weekend: { name: 'Weekend', days: 'Fri–Mon' },
  WeekFri: { name: 'Week', days: 'Fri–Fri' },
  WeekMon: { name: 'Week', days: 'Mon–Mon' },
};

/** The period type a type of stay books. */
export function periodTypeOf(type: TypeOfStay): OnlinePeriodType {
  return type === 'WeekFri' || type === 'WeekMon' ? 'Week' : type;
}

/** A stay [start, end): nights start … end−1. */
export interface Stay {
  start: IsoDate;
  end: IsoDate;
}

/** Inclusive nights [firstNight, lastNight]. */
export interface NightRange {
  firstNight: IsoDate;
  lastNight: IsoDate;
}

const MON = 1;
const FRI = 5;
const SAT = 6;
const SUN = 0;

/** Days that can be clicked in a type of stay (before availability is considered). Every day is clickable in both Weeks. */
export function isClickableDay(type: TypeOfStay, day: IsoDate): boolean {
  const dow = dayOfWeek(day);
  switch (type) {
    case 'Midweek':
      return dow >= MON && dow <= FRI;
    case 'Weekend':
      return dow === FRI || dow === SAT || dow === SUN || dow === MON;
    case 'WeekFri':
    case 'WeekMon':
      return true;
  }
}

/**
 * The period of the type that contains the clicked day, or null when the day isn't clickable. For the Weeks, the week whose
 * nights contain the day, so the start can be earlier than it (Week (Fri–Fri), Thu 15/10/2026 → Fri 09/10 → Fri 16/10).
 */
export function periodForClick(type: TypeOfStay, day: IsoDate): Stay | null {
  if (!isClickableDay(type, day)) return null;
  const dow = dayOfWeek(day);
  switch (type) {
    case 'Midweek': {
      const monday = addDays(day, -(dow - MON));
      return { start: monday, end: addDays(monday, 4) };
    }
    case 'Weekend': {
      const back = dow === FRI ? 0 : dow === SAT ? 1 : dow === SUN ? 2 : 3;
      const friday = addDays(day, -back);
      return { start: friday, end: addDays(friday, 3) };
    }
    case 'WeekFri':
    case 'WeekMon': {
      const startDow = type === 'WeekFri' ? FRI : MON;
      const start = addDays(day, -((dow - startDow + 7) % 7));
      return { start, end: addDays(start, 7) };
    }
  }
}

/** Booking vs booking. */
export function staysOverlap(a: Stay, b: Stay): boolean {
  return a.start < b.end && b.start < a.end;
}

/** Booking vs inclusive nights. */
export function stayOverlapsNights(stay: Stay, nights: NightRange): boolean {
  return stay.start <= nights.lastNight && nights.firstNight < stay.end;
}

export function stayIncludesDay(stay: Stay, day: IsoDate): boolean {
  return day >= stay.start && day <= stay.end;
}

export const PERIOD_LABELS: Record<OnlinePeriodType, { name: string; days: string }> = {
  Midweek: { name: 'Mid-week', days: 'Mon–Fri' },
  Weekend: { name: 'Weekend', days: 'Fri–Mon' },
  Week: { name: 'Week', days: 'Mon–Mon or Fri–Fri' },
};
