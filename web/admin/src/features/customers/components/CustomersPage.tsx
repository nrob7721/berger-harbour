import { Fragment, useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { useToast } from '@shared/components/Toast/Toast';
import { applyServerErrors, rules, useAppForm } from '@shared/lib/forms';
import { errorMessage } from '@shared/lib/http';
import { formatDate, formatTimestamp } from '@shared/utils/dates';
import { formatMobile } from '@shared/utils/phone';
import { PageHeader } from '@/components/Layout/Layout';
import { table } from '@/components/Table/Table';
import { api, keys } from '@/lib/api';
import type { Customer } from '@/lib/types';
import { addressLines } from '@/utils/address';
import styles from './CustomersPage.module.css';

const PERIOD: Record<string, string> = { Midweek: 'Mid-week', Weekend: 'Weekend', Week: 'Week', LongWeekend: 'Long weekend', Custom: 'Custom' };
const PAYMENT: Record<string, string> = { Outstanding: 'Outstanding', DepositPaid: 'Deposit paid', PartPaid: 'Part paid', FullyPaid: 'Fully paid' };

/** Customers. Only customer comments are edited here; contact details and the address are edited from the booking dialog. */
export function CustomersPage({ onOpenBooking }: { onOpenBooking: (bookingId: string) => void }) {
  const customers = useQuery({ queryKey: keys.customers, queryFn: api.customers, staleTime: 15_000 });
  const [filter, setFilter] = useState('');
  const [expanded, setExpanded] = useState<string | null>(null);
  const term = filter.trim().toLowerCase();
  const rows = (customers.data ?? []).filter((c) =>
    !term || `${c.firstName} ${c.lastName} ${c.email} ${c.mobile ?? ''} ${c.secondaryMobile ?? ''} ${formatMobile(c.mobile)}`.toLowerCase().includes(term));

  return (
    <div>
      <PageHeader title="Customers" />
      <input className={styles.filter} placeholder="Filter by name, email or mobile" aria-label="Filter customers" value={filter} onChange={(e) => setFilter(e.target.value)} />
      {customers.isLoading && <p><Spinner /> Loading customers…</p>}
      {customers.isError && <p role="alert">{errorMessage(customers.error)}</p>}
      <div className={`${table.scroll} ${styles.card}`}>
        <table className={table.table}>
          <thead>
            <tr><th aria-label="Expand" /><th>Name</th><th>Email</th><th>Mobile</th><th>Secondary mobile</th><th>Created</th></tr>
          </thead>
          <tbody>
            {rows.map((c) => {
              const open = expanded === c.id;
              return (
                <Fragment key={c.id}>
                  <tr>
                    <td>
                      <button type="button" className={styles.expander} aria-expanded={open} aria-label={`${open ? 'Collapse' : 'Expand'} ${c.firstName} ${c.lastName}`}
                        onClick={() => setExpanded(open ? null : c.id)}>
                        {open ? '▾' : '▸'}
                      </button>
                    </td>
                    <td>{c.firstName} {c.lastName}</td>
                    <td>{c.email}</td>
                    <td>{formatMobile(c.mobile)}</td>
                    <td>{formatMobile(c.secondaryMobile)}</td>
                    <td>{formatTimestamp(c.createdDate)}</td>
                  </tr>
                  {open && (
                    <tr className={styles.detailRow}>
                      <td colSpan={6}><CustomerDetail customer={c} onOpenBooking={onOpenBooking} /></td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
            {customers.data && rows.length === 0 && <tr><td colSpan={6} className={table.muted}>No customers match.</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CustomerDetail({ customer, onOpenBooking }: { customer: Customer; onOpenBooking: (bookingId: string) => void }) {
  const id = useId();
  const queryClient = useQueryClient();
  const toast = useToast();
  const bookings = useQuery({ queryKey: keys.customerBookings(customer.id), queryFn: () => api.customerBookings(customer.id), staleTime: 0 });
  // Validated when it loses focus (§6.1).
  const { register, handleSubmit, setError, formState: { errors } } = useAppForm<{ comments: string }>({ defaultValues: { comments: customer.comments ?? '' } });
  const save = useMutation({
    mutationFn: (comments: string) => api.updateCustomerComments(customer.id, comments.trim() || null, customer.version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.customers });
      toast.show('Customer comments saved.');
    },
    onError: (e) => applyServerErrors(e, setError, ['comments']),
  });
  const submit = handleSubmit((v) => save.mutate(v.comments));
  const commentsError = errors.comments?.message;
  return (
    <div className={styles.detail}>
      <div>
        <h3 className={styles.label}>Address</h3>
        {customer.address
          ? <p className={styles.address} data-testid="customer-address">{addressLines(customer.address).join('\n')}</p>
          : <p className={table.muted} data-testid="customer-address">No address</p>}
      </div>
      <form className={styles.comments} onSubmit={submit} noValidate>
        <label htmlFor={id} className={styles.label}>Customer comments</label>
        <textarea id={id} rows={3} disabled={save.isPending} aria-invalid={!!commentsError || undefined}
          {...register('comments', { validate: rules.maxLength(4000, 'Customer comments') })} />
        {commentsError && <span role="alert" className={styles.error}>{commentsError}</span>}
        <div className={styles.commentActions}>
          {save.isError && !commentsError && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
          <Button variant="primary" size="sm" type="submit" busy={save.isPending} busyLabel="Saving…">Save</Button>
        </div>
      </form>
      <h3 className={styles.label}>Bookings</h3>
      {bookings.isLoading && <p><Spinner /> Loading bookings…</p>}
      {bookings.data?.length === 0 && <p className={table.muted}>No bookings.</p>}
      {bookings.data && bookings.data.length > 0 && (
        <table className={table.table}>
          <thead><tr><th>Reference</th><th>Boat</th><th>Dates</th><th>Period</th><th>Status</th><th>Payment</th></tr></thead>
          <tbody>
            {bookings.data.map((b) => {
              const inactive = b.status === 'Cancelled' || b.status === 'Expired' || b.status === 'Superseded';
              return (
                <tr key={b.id} className={inactive ? table.faded : ''}>
                  <td><button type="button" className={styles.link} onClick={() => onOpenBooking(b.id)}>{b.reference ?? 'Pending payment'}</button></td>
                  <td>{b.boatName}</td>
                  <td>{formatDate(b.startDate)} → {formatDate(b.endDate)}</td>
                  <td>{PERIOD[b.periodType]}</td>
                  <td>{b.status}{b.isStandby ? ' (stand-by)' : ''}</td>
                  <td>{PAYMENT[b.paymentStatus]}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
