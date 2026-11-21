import { createContext, useContext, useEffect, useMemo, useReducer, type Dispatch, type ReactNode } from 'react';
import { periodTypeOf, type OnlinePeriodType, type Stay, type TypeOfStay } from '@shared/utils/periods';
import { clearSavedProgress, writeSavedProgress } from './saved-progress';

export type Step = 'dates' | 'details' | 'addons' | 'summary' | 'payment' | 'complete';

export const STEP_ORDER: readonly Step[] = ['dates', 'details', 'addons', 'summary', 'payment', 'complete'];

const later = (a: Step, b: Step): Step => (STEP_ORDER.indexOf(b) > STEP_ORDER.indexOf(a) ? b : a);

/** Is step `a` after step `b`? */
export const isAfter = (a: Step, b: Step): boolean => STEP_ORDER.indexOf(a) > STEP_ORDER.indexOf(b);

export interface ContactDetails {
  firstName: string;
  lastName: string;
  email: string;
  mobile: string;
}

export interface RoomingAcceptance {
  slug: string;
  adults: number;
  children: number;
  text: string;
}

export interface Hold {
  bookingId: string;
  clientSecret: string;
  sessionId: string;
  /** When Checkout closes; the hold itself lasts a little longer (§8.1). */
  checkoutExpiresAt: string;
}

/**
 * The booking app's step state. Everything else is server data (TanStack Query) or local to a component. All of it except
 * the personal details is also kept as saved progress (§11.2).
 */
export interface BookingState {
  step: Step;
  /** The furthest step reached; a resumed booking opens it (at most the Summary). */
  furthestStep: Step;
  requestedSlug: string | null;
  typeOfStay: TypeOfStay;
  period: Stay | null;
  adults: number;
  children: number;
  spa: boolean;
  pets: boolean;
  /** While a boat is selected, the calendar shows only that boat's availability. */
  selectedSlug: string | null;
  /**
   * The boat most recently deselected because it became ineligible (a period, guests or feature change, a 409 at Payment, or
   * resuming saved progress). The small-screen step action bar shows it greyed with its reason (§11.2). Selecting or deselecting
   * a boat by hand clears it. Not kept as saved progress.
   */
  deselectedSlug: string | null;
  rooming: RoomingAcceptance | null;
  /**
   * The details as entered on Your details, kept when it is left backwards even if invalid or unsaved; the step's Continue
   * (or a forward jump past it) validates them and saves them to the draft.
   */
  details: ContactDetails | null;
  /** Ticked add-ons and their quantities. */
  addons: Record<string, number>;
  draftId: string | null;
  hold: Hold | null;
  /** The booking being confirmed on the Complete step. */
  completed: { bookingId: string; sessionId: string; email: string } | null;
  /** The Payment step was reached; the Dates & boat step clears its caches when shown again. */
  reachedPayment: boolean;
  /** A message to show on the current step (dates taken, add-ons removed, capacity …). */
  notice: string | null;
  /**
   * A later progress step the customer clicked (decision 41): the current step runs its Continue, then each step after it,
   * and the jump stops on the first that can't continue. Also an earlier step clicked on the Payment step, which releases the
   * hold first.
   */
  jumpTarget: Step | null;
  /** A forward jump stopped here because a field is invalid: the step shows its errors when it opens. */
  showErrors: boolean;
}

/** The period type the booking is for (both Week options are `Week`). */
export const periodTypeOfState = (state: Pick<BookingState, 'typeOfStay'>): OnlinePeriodType => periodTypeOf(state.typeOfStay);

export type BookingAction =
  | { type: 'typeOfStay'; typeOfStay: TypeOfStay }
  | { type: 'period'; period: Stay | null }
  | { type: 'guests'; adults: number; children: number }
  | { type: 'filter'; spa?: boolean; pets?: boolean }
  | { type: 'selectBoat'; slug: string | null }
  | { type: 'boatIneligible' }
  | { type: 'acceptRooming'; acceptance: RoomingAcceptance }
  | { type: 'details'; details: ContactDetails }
  | { type: 'draft'; draftId: string }
  | { type: 'draftLost' }
  | { type: 'addons'; addons: Record<string, number> }
  | { type: 'goto'; step: Step; notice?: string | null; showErrors?: boolean }
  | { type: 'jump'; step: Step }
  | { type: 'jumpCancelled' }
  | { type: 'hold'; hold: Hold | null }
  | { type: 'complete'; bookingId: string; sessionId: string; email: string }
  | { type: 'datesTaken' }
  | { type: 'cachesCleared' }
  | { type: 'notice'; notice: string | null };

export function initialState(requestedSlug: string | null, overrides: Partial<BookingState> = {}): BookingState {
  return {
    step: 'dates', furthestStep: 'dates', requestedSlug, typeOfStay: 'Midweek', period: null, adults: 1, children: 0, spa: false, pets: false,
    selectedSlug: null, deselectedSlug: null, rooming: null, details: null, addons: {}, draftId: null, hold: null,
    completed: null, reachedPayment: false, notice: null, jumpTarget: null, showErrors: false, ...overrides,
  };
}

export function bookingReducer(state: BookingState, action: BookingAction): BookingState {
  switch (action.type) {
    case 'typeOfStay':
      // Changing the type of stay, including between the two Weeks, clears the selected period; the selected boat is kept.
      return action.typeOfStay === state.typeOfStay ? state : { ...state, typeOfStay: action.typeOfStay, period: null };
    case 'period':
      return { ...state, period: action.period };
    case 'guests':
      return { ...state, adults: action.adults, children: action.children };
    case 'filter':
      return { ...state, spa: action.spa ?? state.spa, pets: action.pets ?? state.pets };
    case 'selectBoat':
      return { ...state, selectedSlug: action.slug, deselectedSlug: null };
    case 'boatIneligible':
      return state.selectedSlug ? { ...state, selectedSlug: null, deselectedSlug: state.selectedSlug } : state;
    case 'acceptRooming':
      return { ...state, rooming: action.acceptance };
    case 'details':
      return { ...state, details: action.details };
    case 'draft':
      return { ...state, draftId: action.draftId };
    case 'draftLost':
      return { ...state, draftId: null };
    case 'addons':
      return { ...state, addons: action.addons };
    case 'goto':
      return {
        ...state, step: action.step, furthestStep: later(state.furthestStep, action.step), notice: action.notice ?? null,
        reachedPayment: state.reachedPayment || action.step === 'payment', jumpTarget: null, showErrors: action.showErrors ?? false,
      };
    case 'jump':
      // Back to an earlier step goes straight to it, keeping everything entered; leaving Payment releases the hold first
      // (its step does that). A later step is reached through each step's Continue.
      if (!isAfter(action.step, state.step) && state.step !== 'payment') {
        return { ...state, step: action.step, notice: null, jumpTarget: null, showErrors: false };
      }
      return { ...state, jumpTarget: action.step };
    case 'jumpCancelled':
      return { ...state, jumpTarget: null };
    case 'hold':
      return { ...state, hold: action.hold };
    case 'complete':
      return {
        ...state, step: 'complete', hold: null, draftId: null, notice: null, jumpTarget: null,
        completed: { bookingId: action.bookingId, sessionId: action.sessionId, email: action.email },
      };
    case 'datesTaken':
      // Back to Dates & boat with the period, guests and filters kept; the boat (now unavailable) is deselected.
      return {
        ...state, step: 'dates', hold: null, selectedSlug: null, deselectedSlug: state.selectedSlug, reachedPayment: true, jumpTarget: null,
        notice: 'Sorry, those dates were just taken.',
      };
    case 'cachesCleared':
      return { ...state, reachedPayment: false };
    case 'notice':
      return { ...state, notice: action.notice };
  }
}

interface BookingContextValue {
  state: BookingState;
  dispatch: Dispatch<BookingAction>;
}

const BookingContext = createContext<BookingContextValue | null>(null);

export function BookingProvider({ initial, children }: { initial: BookingState; children: ReactNode }) {
  const [state, dispatch] = useReducer(bookingReducer, initial);
  useEffect(() => {
    window.scrollTo?.({ top: 0 });
  }, [state.step]);
  // Saved progress follows the state, and is deleted once the booking reaches Complete.
  useEffect(() => {
    if (state.step === 'complete') clearSavedProgress();
    else writeSavedProgress(state);
  }, [state]);
  const value = useMemo(() => ({ state, dispatch }), [state]);
  return <BookingContext.Provider value={value}>{children}</BookingContext.Provider>;
}

export function useBooking(): BookingContextValue {
  const ctx = useContext(BookingContext);
  if (!ctx) throw new Error('useBooking must be used inside <BookingProvider>');
  return ctx;
}

export function guestsOf(state: Pick<BookingState, 'adults' | 'children'>): number {
  return state.adults + state.children;
}
