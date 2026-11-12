import type { OnlinePeriodType, Stay } from '@shared/utils/periods';
import type { BookingState, RoomingAcceptance, Step } from './booking-state';

/**
 * Saved progress (§11.2, decision 37): the step state kept in `localStorage` so a refresh or a later visit resumes the booking.
 * It never holds the first name, last name, email or mobile (they are read back from the draft), nor the terms tick.
 */
export interface SavedProgress {
  version: 1;
  requestedSlug: string | null;
  periodType: OnlinePeriodType;
  period: Stay | null;
  adults: number;
  children: number;
  spa: boolean;
  pets: boolean;
  selectedSlug: string | null;
  showSelectedOnly: boolean;
  rooming: RoomingAcceptance | null;
  addons: Record<string, number>;
  draftId: string | null;
  furthestStep: Step;
  /** The hold of the Payment step, released when the progress is resumed. */
  hold: { bookingId: string; sessionId: string } | null;
}

export const SAVED_PROGRESS_KEY = 'bh-booking-progress';

const STEPS: readonly Step[] = ['dates', 'details', 'addons', 'summary', 'payment', 'complete'];
const PERIOD_TYPES: readonly OnlinePeriodType[] = ['Midweek', 'Weekend', 'Week'];

export function toSavedProgress(state: BookingState): SavedProgress {
  return {
    version: 1, requestedSlug: state.requestedSlug, periodType: state.periodType, period: state.period, adults: state.adults,
    children: state.children, spa: state.spa, pets: state.pets, selectedSlug: state.selectedSlug, showSelectedOnly: state.showSelectedOnly,
    rooming: state.rooming, addons: state.addons, draftId: state.draftId, furthestStep: state.furthestStep,
    hold: state.step === 'payment' && state.hold ? { bookingId: state.hold.bookingId, sessionId: state.hold.sessionId } : null,
  };
}

const isText = (v: unknown): v is string => typeof v === 'string' && v.length > 0;
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0;
const isNullableText = (v: unknown) => v === null || isText(v);

/** Only a well-formed record is resumed; anything else counts as unreadable. */
function isSavedProgress(v: unknown): v is SavedProgress {
  if (typeof v !== 'object' || v === null) return false;
  const p = v as Record<string, unknown>;
  const period = p.period as Record<string, unknown> | null;
  const hold = p.hold as Record<string, unknown> | null;
  const rooming = p.rooming as Record<string, unknown> | null;
  return p.version === 1
    && isNullableText(p.requestedSlug) && isNullableText(p.selectedSlug) && isNullableText(p.draftId)
    && PERIOD_TYPES.includes(p.periodType as OnlinePeriodType)
    && (period === null || (typeof period === 'object' && isText(period.start) && isText(period.end)))
    && isCount(p.adults) && isCount(p.children) && typeof p.spa === 'boolean' && typeof p.pets === 'boolean'
    && typeof p.showSelectedOnly === 'boolean'
    && (rooming === null || (typeof rooming === 'object' && isText(rooming.slug) && isCount(rooming.adults) && isCount(rooming.children)
      && typeof rooming.text === 'string'))
    && typeof p.addons === 'object' && p.addons !== null && Object.values(p.addons).every(isCount)
    && STEPS.includes(p.furthestStep as Step)
    && (hold === null || (typeof hold === 'object' && isText(hold.bookingId) && isText(hold.sessionId)));
}

/** The saved progress, or null when there is none, or browser storage is unavailable or unreadable (it is then discarded). */
export function readSavedProgress(): SavedProgress | null {
  try {
    const raw = window.localStorage.getItem(SAVED_PROGRESS_KEY);
    if (raw === null) return null;
    const parsed: unknown = JSON.parse(raw);
    if (isSavedProgress(parsed)) return parsed;
  } catch {
    /* unreadable */
  }
  clearSavedProgress();
  return null;
}

export function writeSavedProgress(state: BookingState): void {
  try {
    window.localStorage.setItem(SAVED_PROGRESS_KEY, JSON.stringify(toSavedProgress(state)));
  } catch {
    /* storage unavailable: the booking still works, it just won't resume */
  }
}

export function clearSavedProgress(): void {
  try {
    window.localStorage.removeItem(SAVED_PROGRESS_KEY);
  } catch {
    /* storage unavailable */
  }
}
