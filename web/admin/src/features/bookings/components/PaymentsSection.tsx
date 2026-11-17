import { useId, useState } from 'react';
import { useFieldArray, useFormContext, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { z } from 'zod';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { useAppForm } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatDate, todayInSydney, type IsoDate } from '@shared/utils/dates';
import { formatMoney, sum } from '@shared/utils/money';
import { Badge } from '@/components/Badge/Badge';
import { api } from '@/lib/api';
import type { BookingDetail } from '@/lib/types';
import { paymentDateError, type PaymentForm } from '../utils/bookingForm';
import type { BookingFormValues } from './BookingDialog';
import styles from './BookingDialog.module.css';

const STATUS_LABEL: Record<string, string> = { Outstanding: 'Outstanding', DepositPaid: 'Deposit paid', PartPaid: 'Part paid', FullyPaid: 'Fully paid' };

export interface PaymentsSectionProps {
  /** The saved booking, or null for a new one. */
  detail: BookingDetail | null;
  /** The Sydney date the booking was created (today for a new booking): the earliest a manual payment can be dated. */
  createdOn: IsoDate;
  dirty: boolean;
  /** While the booking saves, nothing in the section can change. */
  saving: boolean;
}

/**
 * The booking's payments (decision 38): a table of payment rows in the form, the payment status and the server-calculated
 * schedule. Adding and removing payments only changes the form; they are saved with the booking. "Send payment link" is an
 * action of its own.
 */
export function PaymentsSection({ detail, createdOn, dirty, saving }: PaymentsSectionProps) {
  const toast = useToast();
  const { control } = useFormContext<BookingFormValues>();
  const { fields, append, remove } = useFieldArray({ control, name: 'payments', keyName: 'key' });
  const rows = (useWatch({ control, name: 'payments' }) ?? []) as PaymentForm[];
  const [adding, setAdding] = useState(false);
  const send = useMutation({
    mutationFn: () => api.sendPaymentLink(detail!.id),
    onSuccess: () => toast.show(`Payment link for ${formatMoney(detail!.amountDueNow)} sent.`),
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });

  const status = detail?.status;
  // Holds (§12.1): nothing changes on a PendingPayment hold or a paid-but-expired one. An Expired hold without payments takes
  // exactly one row, the payment that confirms it.
  const confirming = status === 'Expired' && detail!.payments.length === 0;
  const locked = saving || status === 'PendingPayment' || (status === 'Expired' && !confirming);
  const canAdd = !locked && !(confirming && rows.length > 0);
  const canSendLink = !dirty && status === 'Active' && !detail!.isStandby && detail!.amountOwing > 0;

  return (
    <section className={styles.payments} aria-label="Payments">
      <h3 className={styles.subheading}>
        Payments {detail && <span className={styles.muted}>· {STATUS_LABEL[detail.paymentStatus]}</span>}
      </h3>
      {fields.length === 0 ? <p className={styles.muted}>No payments yet.</p> : (
        <table className={styles.miniTable}>
          <thead><tr><th>Date</th><th>Amount</th><th>Method</th><th>Note</th><th><span className="visually-hidden">Remove</span></th></tr></thead>
          <tbody>
            {fields.map((f, i) => {
              const row = rows[i] ?? f;
              return (
                <tr key={f.key}>
                  <td>{formatDate(row.date)}</td>
                  <td>{formatMoney(row.amount)}</td>
                  <td>{row.method === 'Stripe' ? 'Card (Stripe)' : 'Manual'}</td>
                  <td>
                    {row.note}
                    {row.id === null && <> <Badge tone="warning">Not saved</Badge></>}
                  </td>
                  <td>
                    <Button size="sm" variant="danger" onClick={() => remove(i)} disabled={locked}
                      aria-label={`Remove the ${formatMoney(row.amount)} payment of ${formatDate(row.date)}`}>
                      Remove
                    </Button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {detail && (
        <>
          <h4 className={styles.minorHeading}>Schedule</h4>
          {dirty && <p className={styles.note}>Schedule updates when you save.</p>}
          <ul className={styles.schedule}>
            {detail.schedule.map((s) => (
              <li key={s.kind}>
                {s.label}: {formatMoney(s.requiredCumulative)} by {formatDate(s.dueDate)} — {s.shortfall > 0 ? `${formatMoney(s.shortfall)} to go` : 'paid'}
              </li>
            ))}
          </ul>
        </>
      )}
      <div className={styles.paymentActions}>
        <Button onClick={() => setAdding(true)} disabled={!canAdd}>Add manual payment</Button>
        {detail && (
          <>
            <Button onClick={() => send.mutate()} busy={send.isPending} busyLabel="Sending…" disabled={!canSendLink}>
              Send payment link
            </Button>
            {dirty && status === 'Active' && !detail.isStandby && <span className={styles.muted}>Save your changes first</span>}
          </>
        )}
      </div>
      {adding && (
        <ManualPaymentDialog
          createdOn={createdOn}
          minimum={confirming ? detail!.checkoutAmount : null}
          suggested={confirming ? (detail!.checkoutAmount ?? 0) : Math.max(0, dueNow(detail, rows))}
          onClose={() => setAdding(false)}
          onAdd={(row) => {
            append(row);
            setAdding(false);
          }}
        />
      )}
    </section>
  );
}

/** The saved booking's amount due now, less the rows not saved yet; only used to suggest an amount. */
function dueNow(detail: BookingDetail | null, rows: PaymentForm[]): number {
  if (!detail) return 0;
  return sum([detail.amountDueNow, -sum(rows.filter((r) => r.id === null).map((r) => Number(r.amount) || 0))]);
}

const paymentSchema = (createdOn: IsoDate, minimum: number | null) => z.object({
  amount: z.number({ message: 'Enter the amount.' }).positive('The amount must be more than $0.')
    .refine((v) => minimum === null || v >= minimum, `To confirm this booking, record at least ${formatMoney(minimum ?? 0)}.`),
  date: z.string().min(1, 'Enter the date.').superRefine((v, ctx) => {
    const error = paymentDateError(v, createdOn);
    if (error) ctx.addIssue({ code: 'custom', message: error });
  }),
  note: z.string().max(500, 'The note must be 500 characters or fewer.'),
});
type ManualPaymentValues = { amount: number; date: IsoDate; note: string };

/** Amount, date and note for a new manual payment. Its Add button only adds a row to the booking form; it sends no request. */
function ManualPaymentDialog({ createdOn, minimum, suggested, onClose, onAdd }: {
  createdOn: IsoDate;
  /** Set when the payment confirms an expired hold: the least it must be (the checkout amount). */
  minimum: number | null;
  suggested: number;
  onClose: () => void;
  onAdd: (row: PaymentForm) => void;
}) {
  const ids = { amount: useId(), date: useId(), note: useId() };
  const today = todayInSydney();
  // Each field is validated when it loses focus (§6.1), with the server's date rule.
  const { register, handleSubmit, formState: { errors } } = useAppForm<ManualPaymentValues>({
    resolver: zodResolver(paymentSchema(createdOn, minimum)),
    defaultValues: { amount: suggested || undefined, date: today, note: '' },
  });
  const add = handleSubmit((v) => onAdd({ id: null, amount: v.amount, date: v.date, note: v.note.trim(), method: 'Manual' }));
  return (
    <Dialog open title="Add manual payment" onClose={onClose} size="sm"
      footer={<>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="primary" onClick={add}>Add</Button>
      </>}>
      <form onSubmit={add} noValidate>
        <fieldset className={styles.fieldset}>
          <Field label="Amount" htmlFor={ids.amount} error={errors.amount?.message}
            hint={minimum !== null ? `To confirm this booking, record at least ${formatMoney(minimum)}.` : undefined}>
            <input id={ids.amount} type="number" step="0.01" min={0} className={inputClass} {...register('amount', { valueAsNumber: true })} />
          </Field>
          <Field label="Date" htmlFor={ids.date} error={errors.date?.message}>
            <input id={ids.date} type="date" className={inputClass} min={createdOn} max={today} {...register('date')} />
          </Field>
          <Field label="Note" htmlFor={ids.note} error={errors.note?.message}>
            <input id={ids.note} className={inputClass} placeholder="e.g. Bank transfer" {...register('note')} />
          </Field>
        </fieldset>
      </form>
    </Dialog>
  );
}
