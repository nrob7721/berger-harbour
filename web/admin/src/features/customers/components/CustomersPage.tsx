import { Fragment, useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { useToast } from '@shared/components/Toast/Toast';
import { errorMessage } from '@shared/lib/http';
import { formatDate, formatTimestamp } from '@shared/utils/dates';
import { formatMobile } from '@shared/utils/phone';
import { PageHeader } from '@/components/Layout/Layout';
import { table } from '@/components/Table/Table';
import { api, keys } from '@/lib/api';
import type { Customer } from '@/lib/types';
import styles from './CustomersPage.module.css';

const PERIOD: Record<string, string> = { Midweek: 'Mid-week', Weekend: 'Weekend', Week: 'Week', LongWeekend: 'Long weekend', Custom: 'Custom' };
const PAYMENT: Record<string, string> = { Outstanding: 'Outstanding', DepositPaid: 'Deposit paid', PartPaid: 'Part paid', FullyPaid: 'Fully paid' };

/** Customers. Only customer comments are edited here; contact details are edited from the booking dialog. */
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
  const [comments, setComments] = useState(customer.comments ?? '');
  const bookings = useQuery({ queryKey: keys.customerBookings(customer.id), queryFn: () => api.customerBookings(customer.id), staleTime: 0 });
  const save = useMutation({
    mutationFn: () => api.updateCustomerComments(customer.id, comments || null, customer.version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.customers });
      toast.show('Customer comments saved.');
    },
  });
  return (
    <div className={styles.detail}>
      <div className={styles.comments}>
        <label htmlFor={id} className={styles.label}>Customer comments</label>
        <textarea id={id} rows={3} value={comments} onChange={(e) => setComments(e.target.value)} disabled={save.isPending} />
        <div className={styles.commentActions}>
          {save.isError && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
          <Button variant="primary" size="sm" onClick={() => save.mutate()} busy={save.isPending} busyLabel="Saving…">Save</Button>
        </div>
      </div>
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
