import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { errorMessage } from '@shared/lib/http';
import { formatDate } from '@shared/utils/dates';
import { formatMoney } from '@shared/utils/money';
import { PERIOD_LABELS } from '@shared/utils/periods';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { api } from '@/lib/api';
import { useBooking } from '@/lib/booking-state';
import type { Quote } from '@/lib/types';
import { checkInRow, checkOutRow } from '../utils/rows';
import styles from './SummaryStep.module.css';

export interface SummaryStepProps {
  hireTermsUrl: string;
  /** The Pay button, composed by the app from the deposit-checkout feature. */
  renderPay: (args: { quote: Quote; termsAccepted: boolean }) => ReactNode;
}

export function SummaryStep({ hireTermsUrl, renderPay }: SummaryStepProps) {
  const { state, dispatch } = useBooking();
  const [terms, setTerms] = useState(false);
  const addons = Object.entries(state.addons).map(([addonId, quantity]) => ({ addonId, quantity }));
  const quote = useQuery({
    queryKey: ['quote', state.selectedSlug, state.periodType, state.period?.start, state.adults, state.children, JSON.stringify(addons)],
    queryFn: () => api.quote(state.selectedSlug!, {
      periodType: state.periodType, startDate: state.period!.start, numberOfAdults: state.adults, numberOfChildren: state.children, addons,
    }),
    staleTime: 0,
    retry: false,
  });
  const q = quote.data;

  return (
    <StepLayout
      title="Summary"
      error={quote.isError ? errorMessage(quote.error) : null}
      back={<Button onClick={() => dispatch({ type: 'goto', step: 'addons' })}>Back</Button>}
      action={q ? renderPay({ quote: q, termsAccepted: terms }) : null}
    >
      {state.notice && <div role="alert" className={styles.alert}>{state.notice}</div>}
      {quote.isLoading && <p><Spinner /> Preparing your quote…</p>}
      {q && (
        <>
          <dl className={styles.rows}>
            <dt>Boat</dt>
            <dd>{q.boatName}</dd>
            <dt>Stay</dt>
            <dd>{PERIOD_LABELS[q.periodType as keyof typeof PERIOD_LABELS]?.name ?? q.periodType}</dd>
            <dt>Check-in</dt>
            <dd data-testid="summary-check-in">{checkInRow(q.startDate)}</dd>
            <dt>Check-out</dt>
            <dd data-testid="summary-check-out">{checkOutRow(q.endDate)}</dd>
            <dt>Guests</dt>
            <dd>{q.numberOfAdults} {q.numberOfAdults === 1 ? 'adult' : 'adults'}, {q.numberOfChildren} {q.numberOfChildren === 1 ? 'child' : 'children'}</dd>
            <dt>Season</dt>
            <dd>{q.seasonName}</dd>
            <dt>Hire price</dt>
            <dd>{formatMoney(q.hirePrice)}</dd>
            <dt>Requested add-ons</dt>
            <dd>
              {q.addons.length === 0 ? 'None' : (
                <ul className={styles.addons}>
                  {q.addons.map((a) => (
                    <li key={a.addonId}>
                      {a.name}{a.quantity > 1 ? ` × ${a.quantity}` : ''} – {a.lineTotal === null ? 'price on request' : formatMoney(a.lineTotal)}
                    </li>
                  ))}
                </ul>
              )}
            </dd>
            <dt>Total</dt>
            <dd>{formatMoney(q.totalPrice)}{q.hasPriceOnRequestAddons ? ' (excludes price-on-request add-ons)' : ''}</dd>
          </dl>

          <div className={styles.payNow}>
            <span>Amount payable now (deposit)</span>
            <strong>{formatMoney(q.amountDueAtCheckout)}</strong>
          </div>

          <div>
            <h3 className={styles.subheading}>Balance schedule</h3>
            <ul className={styles.schedule}>
              {q.paymentSchedule.filter((s) => !s.dueAtBooking).map((s) => (
                <li key={s.kind}>
                  {s.label}: {formatMoney(s.requiredCumulative)} paid in total by {formatDate(s.dueDate)}
                </li>
              ))}
            </ul>
            <p className={styles.muted}>We'll email you a payment link before each payment is due.</p>
          </div>

          <p>A {formatMoney(q.securityBond)} security bond applies — details will be provided.</p>

          <label className={styles.terms}>
            <input type="checkbox" checked={terms} onChange={(e) => setTerms(e.target.checked)} />
            <span>
              I accept the{' '}
              <a href={hireTermsUrl} target="_blank" rel="noopener noreferrer">Hire Terms and Procedures</a>
            </span>
          </label>
        </>
      )}
    </StepLayout>
  );
}
