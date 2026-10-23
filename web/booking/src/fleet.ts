import { formatMoney } from '@shared/format';

// The rules behind the "Dates & boat" step, kept out of main.ts so they can be unit tested.

export interface FleetBoat {
  slug: string;
  name: string;
  imageUrl: string | null;
  maxNoOfGuests: number;
  hasSpa: boolean;
  isPetFriendly: boolean;
}

/** The boat whose "Book Now" button was clicked (`?boat=`). It may be inactive, so it is not always in the fleet. */
export interface RequestedBoat extends FleetBoat {
  isActive: boolean;
}

export interface Filters {
  adults: number;
  children: number;
  spa: boolean;
  petFriendly: boolean;
}

/** One boat's result for the selected period (GET /api/availability/period). */
export interface PeriodBoat {
  slug: string;
  available: boolean;
  hirePrice: number | null;
}

/** The selected period's results by slug; null while no period is selected or its results are loading. */
export type PeriodResults = ReadonlyMap<string, PeriodBoat> | null;

/** Everywhere boats are listed: largest first (by max guests), then by name. */
export function inDisplayOrder<T extends FleetBoat>(boats: readonly T[]): T[] {
  return [...boats].sort((a, b) =>
    b.maxNoOfGuests - a.maxNoOfGuests || a.name.localeCompare(b.name, 'en', { sensitivity: 'base' }));
}

/** The guest dropdowns go up to the largest boat in the fleet. */
export function largestBoatGuests(fleet: readonly FleetBoat[]): number {
  return Math.max(1, ...fleet.map((b) => b.maxNoOfGuests));
}

export function fitsGuests(boat: FleetBoat, filters: Filters): boolean {
  return filters.adults + filters.children <= boat.maxNoOfGuests;
}

export function hasFeatures(boat: FleetBoat, filters: Filters): boolean {
  return (!filters.spa || boat.hasSpa) && (!filters.petFriendly || boat.isPetFriendly);
}

/** Active boats that take the guests and have every ticked feature. */
export function candidateBoats(fleet: readonly FleetBoat[], filters: Filters): FleetBoat[] {
  return fleet.filter((b) => fitsGuests(b, filters) && hasFeatures(b, filters));
}

/** The boats the calendar shows: only the selected boat while the checkbox is ticked, otherwise every candidate. */
export function calendarBoats(fleet: readonly FleetBoat[], filters: Filters, selected: string | null, onlySelected: boolean): string[] {
  if (onlySelected && selected) return [selected];
  return candidateBoats(fleet, filters).map((b) => b.slug);
}

/** The chosen boat and the "Show {Boat}'s availability only" checkbox. */
export interface BoatChoice {
  selected: string | null;
  onlySelected: boolean;
}

/** Selecting a card never changes the checkbox; while it is ticked the calendar follows the newly selected boat. */
export function chooseBoat(choice: BoatChoice, slug: string): BoatChoice {
  return { ...choice, selected: slug };
}

/**
 * After the period, guests or a filter changed: a selected boat that is no longer eligible is deselected, and the
 * checkbox is unticked (it is disabled without a selected boat). The period itself is never cleared here.
 */
export function keepChoiceValid(choice: BoatChoice, fleet: readonly FleetBoat[], filters: Filters, period: PeriodResults): BoatChoice {
  if (!choice.selected || isEligible(choice.selected, fleet, filters, period)) return choice;
  return { selected: null, onlySelected: false };
}

/**
 * Whether the selected boat can stay selected: it is active, takes the guests, has every ticked feature and (once the
 * period's results are known) is free for the period.
 */
export function isEligible(slug: string, fleet: readonly FleetBoat[], filters: Filters, period: PeriodResults): boolean {
  const boat = fleet.find((b) => b.slug === slug);
  if (!boat || !fitsGuests(boat, filters) || !hasFeatures(boat, filters)) return false;
  return !period || period.get(slug)?.available === true;
}

/** Why the requested boat cannot be selected (the first that applies), or null when it can. */
export function requestedBoatReason(requested: RequestedBoat, filters: Filters, period: PeriodResults): string | null {
  if (!requested.isActive) return 'Not available for online booking';
  if (period && period.get(requested.slug)?.available !== true) return 'Not available for selected dates';
  if (!fitsGuests(requested, filters)) return `Too small for ${filters.adults + filters.children} guests`;
  if (!hasFeatures(requested, filters)) return "Doesn't match your filters";
  return null;
}

export interface BoatCardView {
  boat: FleetBoat;
  /** Once a period is selected: the boat's hire price for it. */
  hirePrice: number | null;
  /** Greyed with this reason (requested boat only). */
  reason: string | null;
}

export interface BoatListView {
  /** Always first, whatever its state. */
  requested: BoatCardView | null;
  /** The "Other boats available" separator, shown only with a requested boat. */
  separator: boolean;
  others: BoatCardView[];
  /** Shown instead of the other boats when there are none. */
  emptyMessage: string | null;
}

/**
 * The boat cards. Before a period is selected every candidate is listed; once it is, only candidates free for it.
 * The requested boat is always first, greyed with a reason when it cannot be selected.
 */
export function boatList(
  fleet: readonly FleetBoat[], requested: RequestedBoat | null, filters: Filters, period: PeriodResults,
): BoatListView {
  const price = (slug: string) => period?.get(slug)?.hirePrice ?? null;
  const free = (slug: string) => !period || period.get(slug)?.available === true;
  const candidates = inDisplayOrder(candidateBoats(fleet, filters));
  const others = candidates.filter((b) => b.slug !== requested?.slug && free(b.slug))
    .map((boat) => ({ boat, hirePrice: price(boat.slug), reason: null }));

  let emptyMessage: string | null = null;
  if (others.length === 0) {
    if (!period) emptyMessage = requested ? 'No other boats match your filters.' : 'No boats match your filters.';
    else if (candidates.some((b) => free(b.slug))) emptyMessage = 'No other boats are available for these dates.';
    else emptyMessage = 'No boats match your filters for these dates.';
  }

  const reason = requested ? requestedBoatReason(requested, filters, period) : null;
  return {
    requested: requested ? { boat: requested, hirePrice: reason ? null : price(requested.slug), reason } : null,
    separator: !!requested,
    others,
    emptyMessage,
  };
}

export interface CheckboxView {
  label: string;
  checked: boolean;
  disabled: boolean;
}

/** "Show {Boat}'s availability only": disabled and unticked until a boat is selected. */
export function onlySelectedCheckbox(selectedName: string | null, ticked: boolean): CheckboxView {
  return selectedName
    ? { label: `Show ${selectedName}'s availability only`, checked: ticked, disabled: false }
    : { label: "Show the selected boat's availability only", checked: false, disabled: true };
}

/** The selected boat's hire price for the selected period, or a prompt until both are chosen. */
export function baseHireRateText(selected: string | null, period: PeriodResults): string {
  const price = selected ? period?.get(selected)?.hirePrice : null;
  return price === null || price === undefined ? 'Select dates and a boat' : formatMoney(price);
}

/** What the customer accepted in the rooming warning; it holds while the boat and guest numbers stay the same. */
export interface RoomingAcceptance {
  slug: string;
  adults: number;
  children: number;
  text: string;
}

export function roomingAcceptanceHolds(acceptance: RoomingAcceptance | null, slug: string, adults: number, children: number): boolean {
  return !!acceptance && acceptance.slug === slug && acceptance.adults === adults && acceptance.children === children;
}

/** Drops requested add-ons the boat does not offer; returns the ids removed. */
export function removeDisallowedAddons(requested: Map<string, number>, allowedIds: readonly string[]): string[] {
  const removed = [...requested.keys()].filter((id) => !allowedIds.includes(id));
  for (const id of removed) requested.delete(id);
  return removed;
}
