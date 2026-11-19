import { useFormContext, useWatch, type Control } from 'react-hook-form';
import { formatMoney, sum } from '@shared/utils/money';
import { useBoats } from '@/hooks/useReferenceData';
import { breakdownDeposit, priceBreakdown } from '../utils/bookingForm';
import type { BookingFormValues } from './BookingDialog';
import styles from './BookingDialog.module.css';

/** The deposit saving the form will store (§7.3 Deposit), as the breakdown shows it (§12.1). */
export function useFormDeposit(control: Control<BookingFormValues>, storedDeposit: number | null, isPendingHold: boolean): number | null {
  const boats = useBoats();
  const [payments, boatId] = useWatch({ control, name: ['payments', 'boatId'] });
  return breakdownDeposit({
    boatDeposit: boats.data?.find((b) => b.id === boatId)?.effectiveDepositAmount ?? null,
    storedDeposit,
    hasSavedPayment: (payments ?? []).some((r) => r.id !== null),
    isPendingHold,
  });
}

/**
 * Calculated in the browser from the form as it changes, so staff can quote over the phone before saving. Paid and Owing come
 * from the payment rows in the form, saved or not. The Deposit is for information only and is not part of the Total.
 */
export function PriceBreakdown({ storedDeposit, isPendingHold }: {
  /** The booking's stored `DepositAmount`; `null` on a new booking. */
  storedDeposit: number | null;
  isPendingHold: boolean;
}) {
  const { control } = useFormContext<BookingFormValues>();
  const [hirePrice, lines, payments] = useWatch({ control, name: ['hirePrice', 'addonLines', 'payments'] });
  const p = priceBreakdown(hirePrice ?? null, lines ?? [], sum((payments ?? []).map((r) => Number(r.amount) || 0)));
  const deposit = useFormDeposit(control, storedDeposit, isPendingHold);
  return (
    <section className={styles.breakdown} aria-label="Price breakdown" data-testid="price-breakdown">
      <dl>
        <div><dt>Hire price</dt><dd data-testid="breakdown-hire">{formatMoney(p.hire)}</dd></div>
        <div><dt>Deposit</dt><dd data-testid="breakdown-deposit">{deposit === null ? '—' : formatMoney(deposit)}</dd></div>
        <div><dt>Add-ons</dt><dd data-testid="breakdown-addons">{formatMoney(p.addons)}</dd></div>
        <div className={styles.total}><dt>Total</dt><dd data-testid="breakdown-total">{formatMoney(p.total)}</dd></div>
        <div><dt>Paid</dt><dd data-testid="breakdown-paid">{formatMoney(p.paid)}</dd></div>
        <div><dt>Owing</dt><dd data-testid="breakdown-owing">{formatMoney(p.owing)}</dd></div>
      </dl>
      {p.unpriced > 0 && <p className={styles.muted}>{p.unpriced} add-on{p.unpriced === 1 ? '' : 's'} not yet priced</p>}
    </section>
  );
}
