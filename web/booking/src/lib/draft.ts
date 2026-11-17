import { periodTypeOfState, type BookingState } from './booking-state';
import type { DraftStep, UpdateDraftRequest } from './types';

/** The draft as the booking app currently has it, for PUT /api/booking-drafts/{id}. */
export function draftBody(state: BookingState, lastStepReached: DraftStep): UpdateDraftRequest | null {
  if (!state.details || !state.period || !state.selectedSlug) return null;
  return {
    slug: state.selectedSlug,
    periodType: periodTypeOfState(state),
    startDate: state.period.start,
    numberOfAdults: state.adults,
    numberOfChildren: state.children,
    firstName: state.details.firstName,
    lastName: state.details.lastName,
    email: state.details.email,
    mobile: state.details.mobile,
    addons: Object.entries(state.addons).map(([addonId, quantity]) => ({ addonId, quantity })),
    lastStepReached,
  };
}
