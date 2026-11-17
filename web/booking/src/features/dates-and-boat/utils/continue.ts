import type { BookingState } from '@/lib/booking-state';
import type { BoatDetails } from '@/lib/types';
import { roomingWarningText } from './rooming';

export const CHOOSE_DATES_AND_BOAT = 'Choose your dates and a boat to continue.';

export type DatesContinue = { kind: 'ok' } | { kind: 'incomplete' } | { kind: 'rooming'; text: string };

/**
 * The Dates & boat Continue checks (§11.2 step 2), outside the component so a progress-step jump runs the same ones: a period
 * and a boat are chosen, and the rooming warning is accepted for this boat and these guests when they exceed its beds.
 */
export function datesContinue(state: BookingState, boat: BoatDetails | undefined): DatesContinue {
  if (!state.period || !state.selectedSlug || !boat || boat.slug !== state.selectedSlug) return { kind: 'incomplete' };
  const guests = state.adults + state.children;
  const r = state.rooming;
  const accepted = !!r && r.slug === boat.slug && r.adults === state.adults && r.children === state.children;
  if (guests > boat.adultBeds + boat.kidsBeds && !accepted) return { kind: 'rooming', text: roomingWarningText(boat, guests) };
  return { kind: 'ok' };
}
