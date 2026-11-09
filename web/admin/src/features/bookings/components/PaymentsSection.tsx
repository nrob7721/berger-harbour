import { useId, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatDate, formatTimestamp, todayInSydney } from '@shared/utils/dates';
import { formatMoney } from '@shared/utils/money';
import { api, keys } from '@/lib/api';
import type { BookingDetail } from '@/lib/types';
import styles from './BookingDialog.module.css';

const STATUS_LABEL: Record<string, string> = { Outstanding: 'Outstanding', DepositPaid: 'Deposit paid', PartPaid: 'Part paid', FullyPaid: 'Fully paid' };

/** Payments, the payment status and the server-calculated schedule. Its actions are separate from Save changes. */
export function PaymentsSection({ detail, dirty }: { detail: BookingDetail; dirty: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [adding, setAdding] = useState(false);
  const send = useMutation({
    mutationFn: () => api.sendPaymentLink(detail.id),
    onSuccess: () => toast.show(`Payment link for ${formatMoney(detail.amountDueNow)} sent.`),
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  const actionsBlocked = dirty || detail.status !== 'Active';

  return (
    <section className={styles.payments} aria-label="Payments">
      <h3 className={styles.subheading}>Payments <span className={styles.muted}>· {STATUS_LABEL[detail.paymentStatus]}</span></h3>
      {detail.payments.length === 0 ? <p className={styles.muted}>No payments yet.</p> : (
        <table className={styles.miniTable}>
          <thead><tr><th>Date</th><th>Amount</th><th>Method</th><th>Note</th></tr></thead>
          <tbody>
            {detail.payments.map((p) => (
              <tr key={p.id}><td>{formatTimestamp(p.paidAt)}</td><td>{formatMoney(p.amount)}</td><td>{p.method === 'Stripe' ? 'Card (Stripe)' : 'Manual'}</td><td>{p.note}</td></tr>
            ))}
          </tbody>
        </table>
      )}
      <h4 className={styles.minorHeading}>Schedule</h4>
      {dirty && <p className={styles.note}>Schedule updates when you save.</p>}
      <ul className={styles.schedule}>
        {detail.schedule.map((s) => (
          <li key={s.kind}>
            {s.label}: {formatMoney(s.requiredCumulative)} by {formatDate(s.dueDate)} — {s.shortfall > 0 ? `${formatMoney(s.shortfall)} to go` : 'paid'}
          </li>
        ))}
      </ul>
      <div className={styles.paymentActions}>
        <Button onClick={() => setAdding(true)} disabled={actionsBlocked}>Add manual payment</Button>
        <Button onClick={() => send.mutate()} busy={send.isPending} busyLabel="Sending…" disabled={actionsBlocked || detail.amountOwing <= 0 || detail.isStandby}>
          Send payment link
        </Button>
        {dirty && <span className={styles.muted}>Save your changes first</span>}
      </div>
      {adding && (
        <ManualPaymentDialog
          detail={detail}
          onClose={() => setAdding(false)}
          onSaved={(updated) => {
            queryClient.setQueryData(keys.booking(detail.id), updated);
            void queryClient.invalidateQueries({ queryKey: keys.timelineRoot });
            toast.show(`Payment of ${formatMoney(updated.amountPaid - detail.amountPaid)} recorded.`);
            setAdding(false);
          }}
        />
      )}
    </section>
  );
}

const paymentSchema = z.object({
  amount: z.number({ message: 'Enter the amount.' }).positive('The amount must be more than $0.'),
  date: z.string().min(1, 'Enter the date.'),
  note: z.string(),
});
type PaymentForm = z.infer<typeof paymentSchema>;

function ManualPaymentDialog({ detail, onClose, onSaved }: { detail: BookingDetail; onClose: () => void; onSaved: (d: BookingDetail) => void }) {
  const ids = { amount: useId(), date: useId(), note: useId() };
  const { register, handleSubmit, setError, formState: { errors } } = useForm<PaymentForm>({
    resolver: zodResolver(paymentSchema),
    defaultValues: { amount: Math.max(0, detail.amountDueNow) || undefined, date: todayInSydney(), note: '' },
  });
  const save = useMutation({
    mutationFn: (v: PaymentForm) => api.addPayment(detail.id, { amount: v.amount, date: v.date, note: v.note || null, version: detail.version }),
    onSuccess: onSaved,
    onError: (e) => applyServerErrors(e, setError, ['amount', 'date', 'note']),
  });
  const submit = handleSubmit((v) => save.mutate(v));
  return (
    <Dialog open title="Add manual payment" onClose={onClose} locked={save.isPending} size="sm"
      footer={<>
        {save.isError && <span role="alert" className={styles.footerError}>{errorMessage(save.error)}</span>}
        <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
        <Button variant="primary" onClick={submit} busy={save.isPending} busyLabel="Saving…">Save payment</Button>
      </>}>
      <form onSubmit={submit} noValidate>
        <fieldset className={styles.fieldset} disabled={save.isPending}>
          <Field label="Amount" htmlFor={ids.amount} error={errors.amount?.message}>
            <input id={ids.amount} type="number" step="0.01" min={0} className={inputClass} {...register('amount', { valueAsNumber: true })} />
          </Field>
          <Field label="Date" htmlFor={ids.date} error={errors.date?.message}>
            <input id={ids.date} type="date" className={inputClass} {...register('date')} />
          </Field>
          <Field label="Note" htmlFor={ids.note}>
            <input id={ids.note} className={inputClass} placeholder="e.g. Bank transfer" {...register('note')} />
          </Field>
        </fieldset>
      </form>
    </Dialog>
  );
}
