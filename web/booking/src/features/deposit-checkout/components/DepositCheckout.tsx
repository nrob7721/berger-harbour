import { useCallback, useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { ApiError, errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { EmbeddedCheckout } from '@/components/EmbeddedCheckout/EmbeddedCheckout';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { api, keys } from '@/lib/api';
import { useBooking } from '@/lib/booking-state';
import styles from './DepositCheckout.module.css';

/** The Payment step's entry: creates the hold and the Checkout session, then moves to Payment. */
export function PayButton({ amount, termsAccepted }: { amount: number; termsAccepted: boolean }) {
  const { state, dispatch } = useBooking();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const create = useMutation({
    mutationFn: () => api.createBooking({
      draftId: state.draftId!, slug: state.selectedSlug!, periodType: state.periodType, startDate: state.period!.start,
      numberOfAdults: state.adults, numberOfChildren: state.children, firstName: state.details!.firstName, lastName: state.details!.lastName,
      email: state.details!.email, mobile: state.details!.mobile,
      addons: Object.entries(state.addons).map(([addonId, quantity]) => ({ addonId, quantity })),
      roomingWarningAccepted: !!state.rooming, roomingWarningText: state.rooming?.text ?? null, termsAccepted,
    }),
    onSuccess: (hold) => {
      dispatch({ type: 'hold', hold });
      dispatch({ type: 'goto', step: 'payment' });
    },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) {
        queryClient.removeQueries({ queryKey: keys.availabilityRoot });
        queryClient.removeQueries({ queryKey: keys.periodRoot });
        dispatch({ type: 'datesTaken' });
      } else if (e instanceof ApiError && e.fieldError('addons')) {
        dispatch({ type: 'goto', step: 'addons', notice: e.fieldError('addons') });
      } else {
        setError(errorMessage(e));
      }
    },
  });
  return (
    <div className={styles.pay}>
      {error && <span className={styles.error} role="alert">{error}</span>}
      <Button variant="primary" disabled={!termsAccepted} busy={create.isPending} busyLabel="Starting payment…" onClick={() => { setError(null); create.mutate(); }}>
        Pay {formatMoney(amount)}
      </Button>
    </div>
  );
}

/** Whole minutes left until `expiresAt`, rounded up; 0 once it has passed. */
export function minutesLeft(expiresAt: string, now: number): number {
  return Math.max(0, Math.ceil((Date.parse(expiresAt) - now) / 60_000));
}

/** The minutes left on the hold, updated as time passes. Uses the browser's clock. */
function useMinutesLeft(expiresAt: string | undefined): number | null {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!expiresAt) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [expiresAt]);
  return expiresAt ? minutesLeft(expiresAt, now) : null;
}

/** Embedded Checkout for the deposit, with the hold countdown. Back releases the hold. */
export function PaymentStep() {
  const { state, dispatch } = useBooking();
  const hold = state.hold;
  const minutes = useMinutesLeft(hold?.checkoutExpiresAt);
  const timedOut = minutes === 0;
  const complete = useCallback(() => {
    if (hold && state.details) dispatch({ type: 'complete', bookingId: hold.bookingId, sessionId: hold.sessionId, email: state.details.email });
  }, [hold, state.details, dispatch]);

  const release = useMutation({
    mutationFn: () => api.release(hold!.bookingId, state.draftId!),
    onSuccess: () => {
      dispatch({ type: 'hold', hold: null });
      dispatch({ type: 'goto', step: 'summary' });
    },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) complete(); // the payment completed meanwhile
    },
  });

  if (!hold) return null;
  return (
    <StepLayout
      title="Payment"
      error={release.isError && !(release.error instanceof ApiError && release.error.status === 409) ? errorMessage(release.error) : null}
      back={!timedOut && <Button onClick={() => release.mutate()} busy={release.isPending} busyLabel="Going back…">Back</Button>}
    >
      {timedOut ? (
        // Checkout has closed: paying again creates a new hold.
        <div className={styles.timedOut} role="alert">
          <p>Your dates are no longer held. Go back to try again.</p>
          <Button variant="primary" onClick={() => release.mutate()} busy={release.isPending} busyLabel="Going back…">Back</Button>
        </div>
      ) : (
        <>
          {minutes !== null && (
            <p className={styles.countdown} role="status">
              Dates are guaranteed for the next {minutes} minute{minutes === 1 ? '' : 's'}
            </p>
          )}
          {!release.isPending && <EmbeddedCheckout clientSecret={hold.clientSecret} onComplete={complete} />}
        </>
      )}
    </StepLayout>
  );
}
