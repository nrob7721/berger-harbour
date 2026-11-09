import { createContext, useContext, useEffect, useMemo, useReducer, type Dispatch, type ReactNode } from 'react';
import type { OnlinePeriodType, Stay } from '@shared/utils/periods';

export type Step = 'dates' | 'details' | 'addons' | 'summary' | 'payment' | 'complete';

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
}

/** The booking app's step state. Everything else is server data (TanStack Query) or local to a component. */
export interface BookingState {
  step: Step;
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
}

export type BookingAction =
  | { type: 'periodType'; periodType: OnlinePeriodType }
  | { type: 'period'; period: Stay | null }
  | { type: 'guests'; adults: number; children: number }
  | { type: 'filter'; spa?: boolean; pets?: boolean }
  | { type: 'selectBoat'; slug: string | null }
  | { type: 'showSelectedOnly'; value: boolean }
  | { type: 'acceptRooming'; acceptance: RoomingAcceptance }
  | { type: 'details'; details: ContactDetails }
  | { type: 'draft'; draftId: string }
  | { type: 'draftLost' }
  | { type: 'addons'; addons: Record<string, number> }
  | { type: 'goto'; step: Step; notice?: string | null }
  | { type: 'hold'; hold: Hold | null }
  | { type: 'complete'; bookingId: string; sessionId: string; email: string }
  | { type: 'datesTaken' }
  | { type: 'cachesCleared' }
  | { type: 'notice'; notice: string | null };

const DRAFT_KEY = 'bh-booking-draft-id';

export function readDraftId(): string | null {
  try {
    return window.sessionStorage.getItem(DRAFT_KEY);
  } catch {
    return null;
  }
}

function writeDraftId(id: string | null) {
  try {
    if (id) window.sessionStorage.setItem(DRAFT_KEY, id);
    else window.sessionStorage.removeItem(DRAFT_KEY);
  } catch {
    /* storage unavailable: the draft id still lives in memory */
  }
}

export function initialState(requestedSlug: string | null, overrides: Partial<BookingState> = {}): BookingState {
  return {
    step: 'dates', requestedSlug, periodType: 'Midweek', period: null, adults: 1, children: 0, spa: false, pets: false,
    selectedSlug: null, showSelectedOnly: false, rooming: null, details: null, addons: {}, draftId: readDraftId(), hold: null,
    completed: null, reachedPayment: false, notice: null, ...overrides,
  };
}

export function bookingReducer(state: BookingState, action: BookingAction): BookingState {
  switch (action.type) {
    case 'periodType':
      // Changing the period type clears the selected period; the selected boat is kept.
      return action.periodType === state.periodType ? state : { ...state, periodType: action.periodType, period: null };
    case 'period':
      return { ...state, period: action.period };
    case 'guests':
      return { ...state, adults: action.adults, children: action.children };
    case 'filter':
      return { ...state, spa: action.spa ?? state.spa, pets: action.pets ?? state.pets };
    case 'selectBoat':
      // Deselecting also unticks "Show {Boat}'s availability only"; selecting another card never changes it.
      return action.slug === null
        ? { ...state, selectedSlug: null, showSelectedOnly: false }
        : { ...state, selectedSlug: action.slug };
    case 'showSelectedOnly':
      return { ...state, showSelectedOnly: state.selectedSlug ? action.value : false };
    case 'acceptRooming':
      return { ...state, rooming: action.acceptance };
    case 'details':
      return { ...state, details: action.details };
    case 'draft':
      writeDraftId(action.draftId);
      return { ...state, draftId: action.draftId };
    case 'draftLost':
      writeDraftId(null);
      return { ...state, draftId: null };
    case 'addons':
      return { ...state, addons: action.addons };
    case 'goto':
      return {
        ...state, step: action.step, notice: action.notice ?? null,
        reachedPayment: state.reachedPayment || action.step === 'payment',
      };
    case 'hold':
      return { ...state, hold: action.hold };
    case 'complete':
      writeDraftId(null);
      return {
        ...state, step: 'complete', hold: null, draftId: null, notice: null,
        completed: { bookingId: action.bookingId, sessionId: action.sessionId, email: action.email },
      };
    case 'datesTaken':
      // Back to Dates & boat with the period, guests and filters kept; the boat (now unavailable) is deselected.
      return {
        ...state, step: 'dates', hold: null, selectedSlug: null, showSelectedOnly: false, reachedPayment: true,
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
