import { addDays, formatDateWithWeekday, zoneLabel, type IsoDate } from '@shared/utils/dates';
import { isClickableDay, periodForClick, stayOverlapsNights, staysOverlap, type Stay, type TypeOfStay } from '@shared/utils/periods';
import type { BoatSummary, BookingSettings, FleetAvailability, PeriodAvailability } from '@/lib/types';

export interface Filters {
  adults: number;
  children: number;
  spa: boolean;
  pets: boolean;
}

/** A boat as the cards need it (the requested boat may be inactive, so it isn't always in the fleet list). */
export interface CardBoat extends BoatSummary {
  isActive: boolean;
}

export const guests = (f: Pick<Filters, 'adults' | 'children'>) => f.adults + f.children;

export function matchesFilters(boat: Pick<BoatSummary, 'berth' | 'hasSpa' | 'isPetFriendly'>, f: Filters): boolean {
  return guests(f) <= boat.berth && (!f.spa || boat.hasSpa) && (!f.pets || boat.isPetFriendly);
}

/** Active house boats that match guests, Spa and Pet friendly. */
export function candidateBoats(fleet: BoatSummary[], f: Filters): BoatSummary[] {
  return fleet.filter((b) => matchesFilters(b, f));
}

/** The boats the calendar considers: the selected boat while there is one, otherwise all candidate boats. */
export function calendarBoats(candidates: BoatSummary[], fleet: BoatSummary[], selectedSlug: string | null): string[] {
  if (selectedSlug) return fleet.some((b) => b.slug === selectedSlug) ? [selectedSlug] : [];
  return candidates.map((b) => b.slug);
}

export function boatFreeFor(availability: FleetAvailability | undefined, slug: string, stay: Stay): boolean {
  const boat = availability?.boats.find((b) => b.slug === slug);
  if (!boat) return false;
  return !boat.unavailable.some((r) => staysOverlap({ start: r.start, end: r.end }, stay));
}

export function blockedPeriodFor(availability: FleetAvailability | undefined, stay: Stay) {
  return availability?.enquireOnly.find((p) => stayOverlapsNights(stay, p)) ?? null;
}

export function isEnquireOnlyDay(availability: FleetAvailability | undefined, day: IsoDate): boolean {
  return availability?.enquireOnly.some((p) => day >= p.firstNight && day <= p.lastNight) ?? false;
}

/** Why a period can't be chosen, or null when it can. */
export function periodProblem(
  stay: Stay,
  availability: FleetAvailability | undefined,
  boats: string[],
  settings: BookingSettings,
): 'blocked' | 'too-soon' | 'too-late' | 'unavailable' | null {
  if (blockedPeriodFor(availability, stay)) return 'blocked';
  if (stay.start < addDays(settings.today, settings.minimumLeadTimeDays)) return 'too-soon';
  if (addDays(stay.end, -1) > settings.bookingsOpenUntil) return 'too-late';
  if (!boats.some((slug) => boatFreeFor(availability, slug, stay))) return 'unavailable';
  return null;
}

export type DayState =
  | { kind: 'adjacent' }
  | { kind: 'disabled'; enquire: boolean }
  | { kind: 'enquire'; period: Stay }
  | { kind: 'available'; period: Stay };

/** Greying is per period, not per day: a day is disabled when the period it maps to is unavailable or invalid. */
export function dayState(
  day: IsoDate,
  inMonth: boolean,
  type: TypeOfStay,
  availability: FleetAvailability | undefined,
  boats: string[],
  settings: BookingSettings,
): DayState {
  if (!inMonth) return { kind: 'adjacent' };
  const enquire = isEnquireOnlyDay(availability, day);
  if (!isClickableDay(type, day)) return { kind: 'disabled', enquire };
  const period = periodForClick(type, day)!;
  const problem = periodProblem(period, availability, boats, settings);
  if (problem === 'blocked') return { kind: 'enquire', period };
  if (problem) return { kind: 'disabled', enquire };
  return { kind: 'available', period };
}

export type SelectionMark = 'in' | 'out' | 'inner' | null;

export function selectionMark(day: IsoDate, period: Stay | null): SelectionMark {
  if (!period) return null;
  if (day === period.start) return 'in';
  if (day === period.end) return 'out';
  if (day > period.start && day < period.end) return 'inner';
  return null;
}

/**
 * "Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)": one zone label, for the check-out date when a period is selected, or
 * for the visible month (its 15th) when nothing is. A stay that crosses a daylight-saving change takes its check-out date's label.
 */
export function checkInOutLine(period: Stay | null, visibleMonth: IsoDate): string {
  return `Check-in 11am – 2pm · Check-out from 8am (${zoneLabel(period ? period.end : addDays(visibleMonth, 14))})`;
}

/** "Selected: check-in Fri 16/10/2026, check-out Fri 23/10/2026." */
export function selectedLine(period: Stay): string {
  return `Selected: check-in ${formatDateWithWeekday(period.start)}, check-out ${formatDateWithWeekday(period.end)}.`;
}

/** The requested boat's grey reason; the first that applies. Null when it can be selected. */
export function requestedBoatReason(boat: CardBoat, f: Filters, periodResult: PeriodAvailability | undefined): string | null {
  if (!boat.isActive) return 'Not available for online booking';
  if (periodResult && !periodResult.boats.find((b) => b.slug === boat.slug)?.available) return 'Not available for selected dates';
  if (guests(f) > boat.berth) return `Too small for ${guests(f)} guests`;
  if ((f.spa && !boat.hasSpa) || (f.pets && !boat.isPetFriendly)) return "Doesn't match your filters";
  return null;
}

/** Whether the selected boat is still eligible for the period, guests and features. */
export function selectedBoatEligible(
  slug: string,
  fleet: BoatSummary[],
  f: Filters,
  periodResult: PeriodAvailability | undefined,
): boolean {
  const boat = fleet.find((b) => b.slug === slug);
  if (!boat || !matchesFilters(boat, f)) return false;
  if (periodResult && !periodResult.boats.find((b) => b.slug === slug)?.available) return false;
  return true;
}

/** The fleet's largest berth: the most guests the dropdowns offer. */
export function maxBerth(fleet: BoatSummary[]): number {
  return fleet.reduce((max, b) => Math.max(max, b.berth), 1);
}

/**
 * The visible month's availability window: the calendar grid with a week either side, so week stays starting on its last days,
 * and those a click on its first days maps to (starting up to six days earlier), are covered.
 */
export function availabilityWindow(grid: IsoDate[][]): { from: IsoDate; to: IsoDate } {
  return { from: addDays(grid[0][0], -7), to: addDays(grid[grid.length - 1][6], 8) };
}
