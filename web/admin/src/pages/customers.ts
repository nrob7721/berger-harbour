import { formatDate, sydneyDate } from '@shared/dates';
import { clear, h } from '@shared/dom';
import { formatPhone } from '@shared/phone';
import { adminApi, type Customer, type CustomerBooking } from '../api';
import { loading, PAYMENT_STATUS_LABEL, PERIOD_LABEL, showError, STATUS_LABEL } from '../ui';
import { openBookingDialog } from './booking-dialog';

/**
 * Read-only in v1, apart from opening a customer's bookings. Contact details are updated from the booking dialog.
 */
export async function renderCustomers(page: HTMLElement): Promise<void> {
  const filter = h('input', { type: 'search', placeholder: 'Filter by name, email or phone', 'aria-label': 'Filter customers', class: 'search__input' });
  const container = h('div', {});
  const count = h('p', { class: 'muted', 'aria-live': 'polite' });
  page.append(h('div', { class: 'toolbar' }, h('h1', { class: 'toolbar__title' }, 'Customers'), filter), count, container);

  loading(container);
  let customers: Customer[];
  try {
    customers = await adminApi.customers();
  } catch (e) {
    clear(container);
    showError(container, e);
    return;
  }

  const expanded = new Set<string>();
  const phoneDigits = (p: string | null) => (p ? formatPhone(p).replace(/\D/g, '') + p.replace(/\D/g, '') : '');

  const render = () => {
    const term = filter.value.trim().toLowerCase();
    const digits = term.replace(/\D/g, '');
    const rows = customers.filter((c) => !term || c.fullName.toLowerCase().includes(term) || c.email.includes(term) ||
      (digits.length >= 3 && (phoneDigits(c.mobileNumber).includes(digits) || phoneDigits(c.secondaryPhone).includes(digits))));
    count.textContent = `${rows.length} of ${customers.length} customers`;
    clear(container);
    const body = h('tbody');
    for (const c of rows) {
      const detailsId = `customer-bookings-${c.id}`;
      const isOpen = expanded.has(c.id);
      const toggle = h('button', {
        type: 'button', class: 'expander', 'aria-expanded': isOpen ? 'true' : 'false', 'aria-controls': detailsId,
        'aria-label': `Bookings of ${c.fullName}`,
      }, isOpen ? '▾' : '▸');
      const detailsRow = h('tr', { class: 'customer-bookings', id: detailsId, hidden: !isOpen }, h('td', { colspan: 6 }));
      toggle.addEventListener('click', () => {
        const open = !expanded.has(c.id);
        if (open) expanded.add(c.id);
        else expanded.delete(c.id);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.textContent = open ? '▾' : '▸';
        detailsRow.hidden = !open;
        if (open) void renderBookings(detailsRow.firstElementChild as HTMLElement, c);
      });
      body.append(
        h('tr', {},
          h('td', { class: 'nowrap' }, toggle),
          h('td', {}, c.fullName),
          h('td', {}, h('a', { href: `mailto:${c.email}` }, c.email)),
          h('td', { class: 'nowrap' }, formatPhone(c.mobileNumber)),
          h('td', { class: 'nowrap' }, formatPhone(c.secondaryPhone)),
          h('td', { class: 'nowrap' }, formatDate(sydneyDate(c.createdDate)))),
        detailsRow);
      if (isOpen) void renderBookings(detailsRow.firstElementChild as HTMLElement, c);
    }
    container.append(h('div', { class: 'scroll-table' }, h('table', { class: 'table' },
      h('thead', {}, h('tr', {},
        h('th', {}, h('span', { class: 'visually-hidden' }, 'Bookings')), h('th', {}, 'Name'), h('th', {}, 'Email'),
        h('th', {}, 'Mobile'), h('th', {}, 'Secondary phone'), h('th', {}, 'Created'))),
      body)));
  };

  /** All of the customer's bookings (any status, newest first); click to open. */
  async function renderBookings(cell: HTMLElement, customer: Customer): Promise<void> {
    loading(cell, 'Loading bookings…');
    let bookings: CustomerBooking[];
    try {
      bookings = await adminApi.customerBookings(customer.id);
    } catch (e) {
      clear(cell);
      showError(cell, e);
      return;
    }
    clear(cell);
    if (bookings.length === 0) {
      cell.append(h('p', { class: 'muted' }, 'No bookings.'));
      return;
    }
    const refresh = () => void renderBookings(cell, customer);
    cell.append(h('table', { class: 'table table--compact' },
      h('thead', {}, h('tr', {}, h('th', {}, 'Reference'), h('th', {}, 'Boat'), h('th', {}, 'Dates'), h('th', {}, 'Period'),
        h('th', {}, 'Status'), h('th', {}, 'Payment'))),
      h('tbody', {}, ...bookings.map((b) => {
        const inactive = b.status !== 'Active' && b.status !== 'PendingPayment';
        return h('tr', { class: inactive ? 'row--muted' : '' },
          h('td', {}, h('button', { type: 'button', class: 'link-button', onclick: () => void openBookingDialog({ id: b.id, onSaved: refresh }) },
            b.reference)),
          h('td', {}, b.boatName),
          h('td', { class: 'nowrap' }, `${formatDate(b.startDate)} → ${formatDate(b.endDate)}`),
          h('td', {}, PERIOD_LABEL[b.periodType] ?? b.periodType),
          h('td', {}, `${STATUS_LABEL[b.status] ?? b.status}${b.isStandby ? ' · stand-by' : ''}`),
          h('td', {}, PAYMENT_STATUS_LABEL[b.paymentStatus] ?? b.paymentStatus));
      }))));
  }

  filter.addEventListener('input', render);
  render();
}
