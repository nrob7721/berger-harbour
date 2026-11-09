import { useFormContext, useWatch } from 'react-hook-form';
import { formatMoney } from '@shared/utils/money';
import { priceBreakdown } from '../utils/bookingForm';
import type { BookingFormValues } from './BookingDialog';
import styles from './BookingDialog.module.css';

/** Calculated in the browser from the form as it changes, so staff can quote over the phone before saving. */
export function PriceBreakdown({ amountPaid }: { amountPaid: number | null }) {
  const { control } = useFormContext<BookingFormValues>();
  const [hirePrice, lines] = useWatch({ control, name: ['hirePrice', 'addonLines'] });
  const p = priceBreakdown(hirePrice ?? null, lines ?? [], amountPaid);
  return (
    <section className={styles.breakdown} aria-label="Price breakdown" data-testid="price-breakdown">
      <dl>
        <div><dt>Hire price</dt><dd data-testid="breakdown-hire">{formatMoney(p.hire)}</dd></div>
        <div><dt>Add-ons</dt><dd data-testid="breakdown-addons">{formatMoney(p.addons)}</dd></div>
        <div className={styles.total}><dt>Total</dt><dd data-testid="breakdown-total">{formatMoney(p.total)}</dd></div>
        {p.paid !== null && (
          <>
            <div><dt>Paid</dt><dd data-testid="breakdown-paid">{formatMoney(p.paid)}</dd></div>
            <div><dt>Owing</dt><dd data-testid="breakdown-owing">{formatMoney(p.owing)}</dd></div>
          </>
        )}
      </dl>
      {p.unpriced > 0 && <p className={styles.muted}>{p.unpriced} add-on{p.unpriced === 1 ? '' : 's'} not yet priced</p>}
    </section>
  );
}
