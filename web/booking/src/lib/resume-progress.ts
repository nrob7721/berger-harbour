import { ApiError } from '@shared/lib/http';
import { api } from './api';
import { STEP_ORDER, type BookingState, type Step } from './booking-state';
import type { SavedProgress } from './saved-progress';
import type { BoatSummary } from './types';

export const DATES_NO_LONGER_BOOKABLE = 'Your saved dates can no longer be booked online. Please choose new dates.';
export const boatNoLongerAvailable = (boat: string) => `Your saved booking has changed: ${boat} is no longer available for those dates.`;

const atMost = (step: Step, limit: Step): Step => (STEP_ORDER.indexOf(step) > STEP_ORDER.indexOf(limit) ? limit : step);
const isStatus = (e: unknown, status: number) => e instanceof ApiError && e.status === status;

/**
 * Resumes saved progress (§11.2): releases a saved hold, reads the personal details back from the draft, re-checks a saved
 * period, then opens the furthest step reached (at most the Summary). Returns the state to start from, or null when the progress
 * must be discarded (the draft is gone).
 */
export async function resumeProgress(saved: SavedProgress, fleet: BoatSummary[]): Promise<Partial<BookingState> | null> {
  const base: Partial<BookingState> = {
    periodType: saved.periodType, period: saved.period, adults: saved.adults, children: saved.children, spa: saved.spa, pets: saved.pets,
    selectedSlug: saved.selectedSlug, showSelectedOnly: saved.showSelectedOnly && !!saved.selectedSlug, rooming: saved.rooming,
    addons: saved.addons, draftId: saved.draftId, furthestStep: atMost(saved.furthestStep, 'summary'),
  };

  // 1. A hold from the Payment step is released. A 409 means the payment completed meanwhile.
  if (saved.hold && saved.draftId) {
    try {
      await api.release(saved.hold.bookingId, saved.draftId);
    } catch (e) {
      if (isStatus(e, 409)) return { ...base, step: 'complete', completed: { ...saved.hold, email: '' } };
      // Otherwise the hold simply lapses.
    }
  }

  // 2. The personal details come back from the draft; they are never kept in the browser.
  let details: BookingState['details'] = null;
  if (saved.draftId) {
    try {
      details = await api.draft(saved.draftId);
    } catch (e) {
      if (isStatus(e, 404)) return null; // converted, or deleted after the retention period
    }
  }
  let step = atMost(saved.furthestStep, 'summary');
  if (!details && STEP_ORDER.indexOf(step) > STEP_ORDER.indexOf('details')) step = 'details';

  // 3. A saved period is checked again against the online rules and the selected boat's availability.
  if (saved.period) {
    try {
      const result = await api.periodAvailability(saved.periodType, saved.period.start);
      const selected = saved.selectedSlug ? result.boats.find((b) => b.slug === saved.selectedSlug) : undefined;
      if (saved.selectedSlug && !selected?.available) {
        const name = fleet.find((b) => b.slug === saved.selectedSlug)?.name ?? 'Your boat';
        return { ...base, details, selectedSlug: null, showSelectedOnly: false, step: 'dates', notice: boatNoLongerAvailable(name) };
      }
    } catch (e) {
      if (isStatus(e, 400)) return { ...base, details, period: null, step: 'dates', notice: DATES_NO_LONGER_BOOKABLE };
    }
  }

  return { ...base, details, step };
}
