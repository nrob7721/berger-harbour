import { addDays, dayOfWeek, type IsoDate } from './dates';

export type OnlinePeriodType = 'Midweek' | 'Weekend' | 'Week';

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

/** Days that can be clicked in a mode (before availability is considered). */
export function isClickableDay(type: OnlinePeriodType, day: IsoDate): boolean {
  const dow = dayOfWeek(day);
  switch (type) {
    case 'Midweek':
      return dow >= MON && dow <= FRI;
    case 'Weekend':
      return dow === FRI || dow === SAT || dow === SUN || dow === MON;
    case 'Week':
      return dow === MON || dow === FRI;
  }
}

/** The period of the type that contains the clicked day (Week: starting on it), or null when the day isn't clickable. */
export function periodForClick(type: OnlinePeriodType, day: IsoDate): Stay | null {
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
    case 'Week':
      return { start: day, end: addDays(day, 7) };
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
