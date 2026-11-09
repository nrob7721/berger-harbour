import { useCallback, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { errorMessage } from '@shared/lib/http';
import { formatDate } from '@shared/utils/dates';
import { formatMoney } from '@shared/utils/money';
import { EmbeddedCheckout } from '@/components/EmbeddedCheckout/EmbeddedCheckout';
import { Notice } from '@/components/Notice/Notice';
import { api } from '@/lib/api';
import styles from './PayPage.module.css';

/** /pay/?token= — reached from payment links in emails. */
export function PayPage({ token, paid }: { token: string; paid: boolean }) {
  const [done, setDone] = useState(paid);
  const details = useQuery({ queryKey: ['payment-link', token], queryFn: () => api.paymentLink(token), enabled: !!token && !done, retry: false });
  const checkout = useMutation({ mutationFn: () => api.paymentCheckout(token) });
  const complete = useCallback(() => setDone(true), []);

  if (done) {
    return (
      <div className={styles.page}>
        <h2>Thank you</h2>
        <p>Your payment has been received. A receipt will be emailed to you.</p>
      </div>
    );
  }
  if (!token) return <Notice tone="error">This payment link is incomplete. Please use the link from your email.</Notice>;
  if (details.isLoading) return <p><Spinner /> Loading your booking…</p>;
  if (details.isError) return <Notice tone="error">{errorMessage(details.error)}</Notice>;
  const d = details.data!;
  return (
    <div className={styles.page}>
      <h2>Booking {d.reference}</h2>
      <dl className={styles.rows}>
        <dt>Name</dt><dd>{d.customerFirstName}</dd>
        <dt>Boat</dt><dd>{d.boatName}</dd>
        <dt>Dates</dt><dd>{formatDate(d.startDate)} → {formatDate(d.endDate)}</dd>
        <dt>Total</dt><dd>{formatMoney(d.totalPrice)}</dd>
        <dt>Paid</dt><dd>{formatMoney(d.amountPaid)}</dd>
        <dt>Owing</dt><dd>{formatMoney(d.amountOwing)}</dd>
        <dt>Due now</dt><dd><strong>{formatMoney(d.amountDueNow)}</strong></dd>
      </dl>
      {d.amountDueNow <= 0 ? (
        <Notice>Nothing to pay — this booking is fully paid. Thank you!</Notice>
      ) : checkout.data ? (
        <EmbeddedCheckout clientSecret={checkout.data.clientSecret} onComplete={complete} />
      ) : (
        <div className={styles.actions}>
          {checkout.isError && <span role="alert" className={styles.error}>{errorMessage(checkout.error)}</span>}
          <Button variant="primary" onClick={() => checkout.mutate()} busy={checkout.isPending} busyLabel="Starting payment…">
            Pay {formatMoney(d.amountDueNow)}
          </Button>
        </div>
      )}
    </div>
  );
}
