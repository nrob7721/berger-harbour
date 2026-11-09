import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { api } from '@/lib/api';
import { useBooking } from '@/lib/booking-state';
import { closeBooking } from '../utils/close';
import styles from './CompleteStep.module.css';

const POLL_MS = 2000;
const GIVE_UP_MS = 30_000;

/** Waits for the webhook to activate the booking, then shows its reference. */
export function CompleteStep() {
  const { state } = useBooking();
  const completed = state.completed!;
  const [startedAt] = useState(() => Date.now());
  const [gaveUp, setGaveUp] = useState(false);
  const status = useQuery({
    queryKey: ['status', completed.bookingId, completed.sessionId],
    queryFn: () => api.status(completed.bookingId, completed.sessionId),
    refetchInterval: (q) => (q.state.data?.status === 'Active' || gaveUp ? false : POLL_MS),
    retry: 2,
  });
  useEffect(() => {
    const timer = window.setTimeout(() => setGaveUp(true), Math.max(0, GIVE_UP_MS - (Date.now() - startedAt)));
    return () => window.clearTimeout(timer);
  }, [startedAt]);

  const active = status.data?.status === 'Active';
  const email = completed.email || status.data?.email || '';
  return (
    <StepLayout title={active ? 'Booking confirmed' : 'Thank you'} action={<Button variant="primary" onClick={() => closeBooking(state.requestedSlug)}>Close</Button>}>
      {active ? (
        <div className={styles.done}>
          <p className={styles.reference}>
            Your booking reference is <strong data-testid="booking-reference">{status.data!.reference}</strong>
          </p>
          <p>A confirmation email has been sent to {email}</p>
        </div>
      ) : gaveUp ? (
        <p>Your payment was received. A confirmation email with your booking reference will follow shortly{email ? ` at ${email}` : ''}.</p>
      ) : (
        <p className={styles.waiting} role="status"><Spinner /> Confirming…</p>
      )}
    </StepLayout>
  );
}
