import { useCallback, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { ApiError, errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { EmbeddedCheckout } from '@/components/EmbeddedCheckout/EmbeddedCheckout';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { api, keys } from '@/lib/api';
import { useBooking } from '@/lib/booking-state';
import styles from './DepositCheckout.module.css';

/** Step 5's entry: creates the hold and the Checkout session, then moves to Payment. */
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

/** Embedded Checkout for the deposit. Back releases the hold. */
export function PaymentStep() {
  const { state, dispatch } = useBooking();
  const hold = state.hold;
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
      back={<Button onClick={() => release.mutate()} busy={release.isPending} busyLabel="Going back…">Back</Button>}
    >
      {!release.isPending && <EmbeddedCheckout clientSecret={hold.clientSecret} onComplete={complete} />}
    </StepLayout>
  );
}
