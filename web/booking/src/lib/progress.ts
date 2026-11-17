import type { BookingState, Step } from './booking-state';

/**
 * The result of running a step's Continue for a step a forward jump passes (§11.2, decision 41): it can continue (with any
 * changes it made), or the jump stops on that step, which opens with the notice or its field errors.
 */
export type StepOutcome =
  | { ok: true; patch?: Partial<BookingState> }
  | { ok: false; stop: Step; notice?: string; showErrors?: boolean; patch?: Partial<BookingState> };
