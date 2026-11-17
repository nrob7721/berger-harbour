import { useCallback, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { passAddons } from '@/features/addons/utils/continue';
import { passDetails } from '@/features/details/utils/details';
import { isAfter, STEP_ORDER, useBooking, type BookingState, type Step } from '@/lib/booking-state';
import type { StepOutcome } from '@/lib/progress';

/**
 * Moves on after a step's Continue succeeded. Normally that opens the next step. During a forward jump on the progress steps
 * (decision 41), the Continue of each step between it and the clicked step runs too, in order, and the jump stops on the
 * first that can't continue; otherwise the clicked step opens.
 */
export function useAdvance() {
  const { state, dispatch } = useBooking();
  const queryClient = useQueryClient();
  const latest = useRef(state);
  latest.current = state;

  const apply = useCallback((patch: Partial<BookingState> | undefined) => {
    if (patch?.addons) dispatch({ type: 'addons', addons: patch.addons });
    if (patch && 'draftId' in patch && patch.draftId === null) dispatch({ type: 'draftLost' });
  }, [dispatch]);

  return useCallback(async (next: Step, changes: Partial<BookingState> = {}) => {
    let working: BookingState = { ...latest.current, ...changes };
    const target = working.jumpTarget;
    let at = next;
    while (target && isAfter(target, at)) {
      const outcome: StepOutcome = at === 'details' ? await passDetails(working)
        : at === 'addons' ? await passAddons(working, queryClient)
        : { ok: true };
      apply(outcome.patch);
      working = { ...working, ...outcome.patch };
      if (!outcome.ok) {
        dispatch({ type: 'goto', step: outcome.stop, notice: outcome.notice ?? null, showErrors: outcome.showErrors });
        return;
      }
      at = STEP_ORDER[STEP_ORDER.indexOf(at) + 1];
    }
    dispatch({ type: 'goto', step: at });
  }, [apply, dispatch, queryClient]);
}
