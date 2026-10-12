import { sydneyZoneLabel, type IsoDate } from '@shared/dates';

// Small pure rules behind the booking steps, kept out of main.ts so they can be unit tested.

/** Adults: 1 … max guests. */
export function adultOptions(maxGuests: number): number[] {
  return Array.from({ length: Math.max(1, maxGuests) }, (_, i) => i + 1);
}

/** Children (under 12): 0 … max guests − adults, so the total never exceeds max guests. */
export function childOptions(maxGuests: number, adults: number): number[] {
  return Array.from({ length: Math.max(0, maxGuests - adults) + 1 }, (_, i) => i);
}

/** Changing adults clamps children so adults + children never exceeds max guests. */
export function clampChildren(maxGuests: number, adults: number, children: number): number {
  return Math.max(0, Math.min(children, maxGuests - adults));
}

/** The rooming warning applies when adults + children is more than adult beds + kids beds. */
export function needsRoomingWarning(adults: number, children: number, adultBeds: number | null, kidsBeds: number | null): boolean {
  return adultBeds !== null && adults + children > adultBeds + (kidsBeds ?? 0);
}

export interface CapacityView {
  /** Fully booked for the dates: the add-on is not shown at all. */
  hidden: boolean;
  /** "6 car spots remaining", or null when the add-on has no capacity. */
  text: string | null;
  /** The quantity input's maximum. */
  max: number;
}

/** How a capacity-limited add-on (e.g. car parking) is shown for the selected dates. */
export function capacityView(addon: { hasCapacity: boolean; capacityUnitLabel: string | null }, remaining: number | null | undefined): CapacityView {
  if (!addon.hasCapacity || remaining === null || remaining === undefined) return { hidden: false, text: null, max: 99 };
  if (remaining <= 0) return { hidden: true, text: null, max: 0 };
  const text = addon.capacityUnitLabel ? `${remaining} ${addon.capacityUnitLabel} remaining` : `${remaining} available for your dates`;
  return { hidden: false, text, max: Math.min(99, remaining) };
}

/**
 * "Check-in 11am – 2pm · Check-out by 8am (AEDT)", with the time zone for the stay's dates (or for the given month when
 * nothing is selected). When daylight saving changes during the stay, each time gets its own label.
 */
export function checkInOutText(times: { checkInTime: string; checkInWindowEnd: string; checkOutTime: string },
  checkInDate: IsoDate, checkOutDate: IsoDate = checkInDate): string {
  const inZone = sydneyZoneLabel(checkInDate);
  const outZone = sydneyZoneLabel(checkOutDate);
  const checkIn = `Check-in ${times.checkInTime} – ${times.checkInWindowEnd}`;
  const checkOut = `Check-out by ${times.checkOutTime}`;
  return inZone === outZone ? `${checkIn} · ${checkOut} (${inZone})` : `${checkIn} ${inZone} · ${checkOut} ${outZone}`;
}
