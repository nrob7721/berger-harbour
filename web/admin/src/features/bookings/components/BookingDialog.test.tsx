import { describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, delay } from 'msw';
import { api, customer, detail, server } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { addDays, formatDate, todayInSydney } from '@shared/utils/dates';
import { BookingDialog } from './BookingDialog';

const writes = () => api.calls.filter((c) => c.method !== 'GET');

async function openExisting(overrides = {}) {
  api.booking = detail(overrides);
  const onClose = vi.fn();
  renderAdmin(<BookingDialog bookingId="bk-1" onClose={onClose} />);
  await screen.findByRole('button', { name: 'Save changes' });
  await screen.findByRole('option', { name: 'Ice' }); // add-ons loaded
  return { onClose, user: userEvent.setup() };
}

describe('Booking dialog: add-on lines', () => {
  it('adding, editing and removing lines sends no request and keeps other fields', async () => {
    const { user } = await openExisting();
    const comments = screen.getByLabelText('Booking comments');
    await user.type(comments, 'Bring extra ice');
    const before = api.calls.length;

    await user.selectOptions(screen.getByLabelText('Add-on to add'), 'hamper');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    fireEvent.change(within(screen.getByTestId('line-0')).getByLabelText('Quantity'), { target: { value: '3' } });
    await user.click(screen.getByRole('button', { name: 'Remove Welcome hamper' }));

    expect(api.calls.slice(before).filter((c) => c.method !== 'GET')).toHaveLength(0);
    expect(api.calls.slice(before).filter((c) => !c.path.startsWith('/api/addons/'))).toHaveLength(0);
    expect(comments).toHaveValue('Bring extra ice');
    expect(screen.getByLabelText('Mobile')).toHaveValue('0412 345 678');
  });

  it('pre-fills new lines with the Settings price for the period type, empty for price on request', async () => {
    const { user } = await openExisting(); // Midweek
    await user.selectOptions(screen.getByLabelText('Add-on to add'), 'ice');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    expect(within(screen.getByTestId('line-1')).getByLabelText('Unit price')).toHaveValue(5);
    await user.selectOptions(screen.getByLabelText('Add-on to add'), 'hamper');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    expect(within(screen.getByTestId('line-2')).getByLabelText('Unit price')).toHaveValue(null);
    expect(screen.getByTestId('line-total-2')).toHaveTextContent('Price TBC');
  });

  it('leaves the price empty for Custom bookings', async () => {
    const { user } = await openExisting({ periodType: 'Custom' });
    await user.selectOptions(screen.getByLabelText('Add-on to add'), 'ice');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    expect(within(screen.getByTestId('line-1')).getByLabelText('Unit price')).toHaveValue(null);
  });

  it('updates line totals and the price breakdown as staff type', async () => {
    await openExisting();
    expect(screen.getByTestId('line-total-0')).toHaveTextContent('$10.00');
    expect(screen.getByTestId('breakdown-total')).toHaveTextContent('$4,010.00');
    expect(screen.getByTestId('breakdown-paid')).toHaveTextContent('$1,000.00');
    expect(screen.getByTestId('breakdown-owing')).toHaveTextContent('$3,010.00');
    fireEvent.change(within(screen.getByTestId('line-0')).getByLabelText('Quantity'), { target: { value: '4' } });
    fireEvent.change(within(screen.getByTestId('line-0')).getByLabelText('Unit price'), { target: { value: '6.5' } });
    expect(screen.getByTestId('line-total-0')).toHaveTextContent('$26.00');
    expect(screen.getByTestId('breakdown-addons')).toHaveTextContent('$26.00');
    fireEvent.change(screen.getByLabelText('Hire price'), { target: { value: '5000' } });
    expect(screen.getByTestId('breakdown-total')).toHaveTextContent('$5,026.00');
    expect(screen.getByTestId('breakdown-owing')).toHaveTextContent('$4,026.00');
    fireEvent.change(within(screen.getByTestId('line-0')).getByLabelText('Unit price'), { target: { value: '' } });
    expect(screen.getByText('1 add-on not yet priced')).toBeInTheDocument();
    expect(screen.getByTestId('breakdown-total')).toHaveTextContent('$5,000.00');
  });
});

const today = todayInSydney();

/** Opens Add manual payment, fills it and presses its Add button. */
async function addPayment(user: ReturnType<typeof userEvent.setup>, amount: string, note = '', date?: string) {
  await user.click(screen.getByRole('button', { name: 'Add manual payment' }));
  const dialog = await screen.findByRole('dialog', { name: 'Add manual payment' });
  fireEvent.change(within(dialog).getByLabelText('Amount'), { target: { value: amount } });
  if (date) fireEvent.change(within(dialog).getByLabelText('Date'), { target: { value: date } });
  if (note) await user.type(within(dialog).getByLabelText('Note'), note);
  await user.click(within(dialog).getByRole('button', { name: 'Add' }));
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add manual payment' })).not.toBeInTheDocument());
}

describe('Booking dialog: payments', () => {
  it('Add manual payment adds a "Not saved" row and sends no request; Paid and Owing follow the rows', async () => {
    const { user } = await openExisting();
    const comments = screen.getByLabelText('Booking comments');
    await user.type(comments, 'Keep me');
    const before = writes().length;
    await addPayment(user, '500', 'Cash');
    const table = within(screen.getByRole('region', { name: 'Payments' })).getByRole('table');
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Date', 'Amount', 'Method', 'Note', 'Remove']);
    const row = within(table).getByText('Cash').closest('tr')!;
    expect(row).toHaveTextContent('Not saved');
    expect(row).toHaveTextContent('$500.00');
    expect(screen.getByTestId('breakdown-paid')).toHaveTextContent('$1,500.00');
    expect(screen.getByTestId('breakdown-owing')).toHaveTextContent('$2,510.00');
    expect(writes()).toHaveLength(before);
    expect(comments).toHaveValue('Keep me');
  });

  it('Remove takes Stripe and manual rows out of the form without a request', async () => {
    const { user } = await openExisting({
      payments: [
        { id: 'p-1', amount: 1000, method: 'Stripe', paidAt: '2026-08-02T02:00:00Z', note: null, recordedBy: 'Customer' },
        { id: 'p-2', amount: 200, method: 'Manual', paidAt: '2026-08-03T02:00:00Z', note: 'Cash', recordedBy: 'SystemUser' },
      ],
    });
    expect(screen.getByText('Card (Stripe)')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Remove the $1,000.00 payment of 02/08/2026' }));
    await user.click(screen.getByRole('button', { name: 'Remove the $200.00 payment of 03/08/2026' }));
    expect(screen.getByText('No payments yet.')).toBeInTheDocument();
    expect(screen.getByTestId('breakdown-paid')).toHaveTextContent('$0.00');
    expect(writes()).toHaveLength(0);
  });

  it('checks the date rule when the date loses focus: not before the booking was created, not in the future', async () => {
    const { user } = await openExisting(); // created 01/08/2026
    await user.click(screen.getByRole('button', { name: 'Add manual payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add manual payment' });
    const date = within(dialog).getByLabelText('Date');
    expect(date).toHaveValue(today);
    fireEvent.change(date, { target: { value: '2026-07-31' } });
    fireEvent.blur(date);
    expect(await within(dialog).findByText("The payment date can't be before the booking was created (01/08/2026).")).toBeInTheDocument();
    fireEvent.change(date, { target: { value: addDays(today, 1) } });
    expect(await within(dialog).findByText("The payment date can't be in the future.")).toBeInTheDocument();
    fireEvent.change(date, { target: { value: '2026-08-01' } });
    await waitFor(() => expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument());
  });

  it('saves payments only with Save changes, with the rest of the booking', async () => {
    const { onClose, user } = await openExisting();
    await addPayment(user, '250', 'Bank transfer');
    await user.click(screen.getByRole('button', { name: 'Remove the $1,000.00 payment of 02/08/2026' }));
    expect(writes()).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(writes()).toHaveLength(1);
    expect(writes()[0].body).toMatchObject({ payments: [{ id: null, amount: 250, date: today, note: 'Bank transfer' }] });
  });

  it('shows the payments section on a new booking, where the only valid date is today', async () => {
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    expect(screen.getByRole('region', { name: 'Payments' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send payment link' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add manual payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add manual payment' });
    const date = within(dialog).getByLabelText('Date');
    fireEvent.change(date, { target: { value: addDays(today, -1) } });
    fireEvent.blur(date);
    expect(await within(dialog).findByText(`The payment date can't be before the booking was created (${formatDate(today)}).`)).toBeInTheDocument();
  });

  it('disables Send payment link while there are unsaved changes, and notes the schedule', async () => {
    const { user } = await openExisting();
    expect(screen.getByRole('button', { name: 'Send payment link' })).toBeEnabled();
    expect(screen.queryByText('Schedule updates when you save.')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Booking comments'), 'x');
    expect(screen.getByRole('button', { name: 'Add manual payment' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Send payment link' })).toBeDisabled();
    expect(screen.getByText('Save your changes first')).toBeInTheDocument();
    expect(screen.getByText('Schedule updates when you save.')).toBeInTheDocument();
  });

  it('disables Send payment link when nothing is owing', async () => {
    await openExisting({ amountOwing: 0, paymentStatus: 'FullyPaid' });
    expect(screen.getByRole('button', { name: 'Send payment link' })).toBeDisabled();
  });
});

describe('Booking dialog: price breakdown deposit', () => {
  const deposit = () => screen.getByTestId('breakdown-deposit');

  it('shows "—" on a new booking until a boat is chosen, then the boat\'s effective deposit', async () => {
    renderAdmin(<BookingDialog bookingId={null} onClose={() => {}} />);
    const user = userEvent.setup();
    await screen.findByRole('option', { name: 'Misty Blue' });
    expect(deposit()).toHaveTextContent('—');
    await user.selectOptions(screen.getByLabelText('Boat'), 'b-mb');
    expect(deposit()).toHaveTextContent('$1,500.00');
    await user.selectOptions(screen.getByLabelText('Boat'), 'b-pb');
    expect(deposit()).toHaveTextContent('$1,000.00'); // the Settings default
    // Not part of the Total: there are no add-on lines, so the Total is the hire price.
    expect(screen.getByTestId('breakdown-total').textContent).toBe(screen.getByTestId('breakdown-hire').textContent);
  });

  it('follows the chosen boat while the form has no saved payment rows, including with only an unsaved row', async () => {
    const { user } = await openExisting({ payments: [], amountPaid: 0, depositAmount: 900 });
    expect(deposit()).toHaveTextContent('$1,000.00');
    await addPayment(user, '200');
    await user.selectOptions(screen.getByLabelText('Boat'), 'b-mb');
    expect(deposit()).toHaveTextContent('$1,500.00');
    expect(screen.getByTestId('breakdown-total')).toHaveTextContent('$4,010.00');
  });

  it('keeps the stored deposit while a saved payment row remains, and follows the boat once they are all removed', async () => {
    const { user } = await openExisting({ depositAmount: 900 });
    expect(deposit()).toHaveTextContent('$900.00');
    await user.selectOptions(screen.getByLabelText('Boat'), 'b-mb');
    expect(deposit()).toHaveTextContent('$900.00');
    await user.click(screen.getByRole('button', { name: 'Remove the $1,000.00 payment of 02/08/2026' }));
    expect(deposit()).toHaveTextContent('$1,500.00');
  });

  it('shows the stored deposit on holds', async () => {
    await openExisting({ ...onlineHold, status: 'PendingPayment', holdExpiresAt: '2026-10-01T03:40:00Z', depositAmount: 800 });
    expect(deposit()).toHaveTextContent('$800.00');
  });
});

describe('Booking dialog: customer', () => {
  it('shows the Frequent Floater badge with the count', async () => {
    await openExisting({ frequentFloater: { isFrequentFloater: true, otherBookingCount: 2 } });
    expect(screen.getByText('⭐ Frequent Floater')).toHaveAttribute('title', '2 other bookings within a year');
  });

  it('pre-fills names, mobiles, address and customer comments from an email lookup and flags a Frequent Floater', async () => {
    api.lookups['jane@example.com'] = {
      customer: { ...customer, address: { line1: '1 River Rd', line2: null, locality: 'Mannum', state: 'SA', postcode: '5238', country: 'AU' } },
      frequentFloater: { isFrequentFloater: true, otherBookingCount: 1 },
    };
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('First name'), 'Typed');
    await user.type(screen.getByLabelText('City/Locality'), 'Typed town');
    await user.type(screen.getByLabelText('Email'), 'jane@example.com');
    await user.tab();
    await waitFor(() => expect(screen.getByLabelText('First name')).toHaveValue('Jane')); // always replaces what was typed
    expect(screen.getByLabelText('Customer comments')).toHaveValue('Likes the top deck');
    expect(screen.getByLabelText('Address line 1')).toHaveValue('1 River Rd');
    expect(screen.getByLabelText('City/Locality')).toHaveValue('Mannum');
    expect(screen.getByLabelText('State')).toHaveValue('SA');
    expect(screen.getByLabelText('Postcode')).toHaveValue('5238');
    expect(screen.getByLabelText('Country')).toHaveValue('AU');
    expect(screen.getByText('⭐ Frequent Floater')).toHaveAttribute('title', '1 other booking within a year');
  });

  it('never fills, replaces or clears the secondary mobile on an email lookup: it belongs to the booking', async () => {
    api.lookups['jane@example.com'] = { customer, frequentFloater: { isFrequentFloater: false, otherBookingCount: 0 } };
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Secondary mobile'), '0412 000 222');
    await user.type(screen.getByLabelText('Email'), 'jane@example.com');
    await user.tab();
    await waitFor(() => expect(screen.getByLabelText('First name')).toHaveValue('Jane'));
    expect(screen.getByLabelText('Secondary mobile')).toHaveValue('0412 000 222');
  });

  it("shows the booking's own secondary mobile and sends it with the booking, not with the customer", async () => {
    const { onClose, user } = await openExisting({ secondaryMobile: '+61412000222' });
    expect(screen.getByLabelText('Secondary mobile')).toHaveValue('0412 000 222');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(writes()[0].body).toMatchObject({ secondaryMobile: '+61412000222' });
  });

  it('validates the email on blur and makes no lookup when it is invalid', async () => {
    api.lookups['jane@gmail'] = { customer, frequentFloater: { isFrequentFloater: false, otherBookingCount: 0 } };
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Email'), 'jane@gmail');
    await user.tab();
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
    expect(api.calls.some((c) => c.path.startsWith('/api/customers/by-email'))).toBe(false);
    expect(screen.getByLabelText('First name')).toHaveValue('');
    await user.type(screen.getByLabelText('Email'), '.com');
    await waitFor(() => expect(screen.queryByText('Enter a valid email address.')).not.toBeInTheDocument());
  });

  it('shows an invalid mobile as soon as the field loses focus', async () => {
    const { user } = await openExisting();
    const secondary = screen.getByLabelText('Secondary mobile');
    await user.type(secondary, '02 9876 5432');
    await user.tab();
    expect(await screen.findByText('Enter a valid mobile number for the selected country.')).toBeInTheDocument();
  });

  it('shows a spinner and disables the other customer fields and the main button while the lookup runs', async () => {
    let release!: () => void;
    server.use(http.get('*/api/customers/by-email', async () => {
      await new Promise<void>((resolve) => { release = resolve; });
      return HttpResponse.json({ customer, frequentFloater: { isFrequentFloater: false, otherBookingCount: 0 } });
    }));
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Email'), 'jane@example.com');
    await user.tab();
    // The spinner sits right of the email input, on its line, announced by its accessible label.
    const status = await screen.findByRole('status', { name: 'Looking up customer…' });
    const email = screen.getByLabelText('Email');
    expect(email.parentElement).toContainElement(status);
    expect(email.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    for (const label of ['First name', 'Last name', 'Mobile', 'Address line 1', 'Country', 'Customer comments'])
      expect(screen.getByLabelText(label)).toBeDisabled();
    // The secondary mobile belongs to the booking, not the customer, so the lookup never touches it.
    expect(screen.getByLabelText('Secondary mobile')).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Create booking' })).toBeDisabled();
    release();
    await waitFor(() => expect(screen.queryByText('Looking up customer…')).not.toBeInTheDocument());
    expect(screen.getByLabelText('First name')).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Create booking' })).toBeEnabled();
  });

  it('shows an error on Secondary mobile when it is the same number as the mobile', async () => {
    const { user } = await openExisting(); // mobile +61412345678
    await user.type(screen.getByLabelText('Secondary mobile'), '0412 345 678');
    await user.tab();
    expect(await screen.findByText("The secondary mobile can't be the same as the mobile.")).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Mobile'));
    await user.type(screen.getByLabelText('Mobile'), '0412 000 111');
    await user.tab();
    await waitFor(() => expect(screen.queryByText("The secondary mobile can't be the same as the mobile.")).not.toBeInTheDocument());
  });

  it('keeps what was typed when the email lookup finds no customer', async () => {
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('First name'), 'Ann');
    await user.type(screen.getByLabelText('Address line 1'), '9 Jetty Rd');
    await user.type(screen.getByLabelText('Email'), 'new@example.com');
    await user.tab();
    await waitFor(() => expect(api.calls.some((c) => c.path.startsWith('/api/customers/by-email'))).toBe(true));
    expect(screen.getByLabelText('First name')).toHaveValue('Ann');
    expect(screen.getByLabelText('Address line 1')).toHaveValue('9 Jetty Rd');
  });
});

describe('Booking dialog: address', () => {
  it('needs no part, defaults to Australia with a State dropdown and a 4-digit postcode, and is free text elsewhere', async () => {
    const { user } = await openExisting();
    for (const label of ['Address line 1', 'Address line 2', 'City/Locality', 'Postcode']) expect(screen.getByLabelText(label)).toHaveValue('');
    expect(screen.getByLabelText('Country')).toHaveValue('AU');
    const state = screen.getByLabelText('State');
    expect(state.tagName).toBe('SELECT');
    expect(within(state).getAllByRole('option').map((o) => o.textContent)).toEqual(['—', 'NSW', 'VIC', 'QLD', 'SA', 'WA', 'TAS', 'NT', 'ACT']);
    const postcode = screen.getByLabelText('Postcode');
    await user.type(postcode, '50A');
    await user.tab();
    expect(await screen.findByText('Enter a 4-digit postcode.')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Country'), 'NZ');
    expect(screen.getByLabelText('State').tagName).toBe('INPUT');
    await user.type(screen.getByLabelText('State'), 'Otago');
    await user.clear(postcode);
    await user.type(postcode, '9016');
    await user.tab();
    await waitFor(() => expect(screen.queryByText('Enter a 4-digit postcode.')).not.toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText('Country'), 'AU'); // Otago isn't an Australian state: cleared
    expect(screen.getByLabelText('State')).toHaveValue('');
  });

  it('shows the stored country of an existing customer', async () => {
    await openExisting({ customer: { ...customer, address: { line1: '10 Downing St', line2: null, locality: 'London', state: null, postcode: 'SW1A 2AA', country: 'GB' } } });
    expect(screen.getByLabelText('Country')).toHaveValue('GB');
    expect(screen.getByLabelText('Postcode')).toHaveValue('SW1A 2AA');
  });
});

const onlineHold = {
  customer: null, createdBy: 'Customer' as const, reference: null, payments: [], amountPaid: 0, paymentStatus: 'Outstanding' as const,
  customerDetails: { firstName: 'Ann', lastName: 'Lee', email: 'ann@example.com', mobileNumber: '+61412000111' },
};

describe('Booking dialog: holds', () => {
  it('a PendingPayment hold is fully read-only with its expiry banner', async () => {
    await openExisting({ ...onlineHold, status: 'PendingPayment', holdExpiresAt: '2026-10-01T03:40:00Z' });
    expect(screen.getByText("Waiting for the customer's online payment. The hold expires at 01/10/2026 1:40pm.")).toBeInTheDocument();
    for (const label of ['Boat', 'Email', 'First name', 'Mobile', 'Address line 1', 'Country', 'Booking comments', 'Customer comments', 'Hire price'])
      expect(screen.getByLabelText(label)).toBeDisabled();
    for (const name of ['Save changes', 'Add manual payment', 'Send payment link', 'Recalculate from rates'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(screen.getByLabelText('Email')).toHaveValue('ann@example.com');
    expect(screen.getByText(/Ann Lee · ann@example.com/)).toBeInTheDocument();
  });

  it('an Expired hold takes one manual payment row of at least the checkout amount, confirmed with Confirm booking', async () => {
    api.booking = detail({ ...onlineHold, status: 'Expired', checkoutAmount: 1000 });
    renderAdmin(<BookingDialog bookingId="bk-1" onClose={() => {}} />);
    const user = userEvent.setup();
    const confirm = await screen.findByRole('button', { name: 'Confirm booking' });
    expect(screen.getByText('This hold expired without payment. Record a manual payment of at least $1,000.00 to confirm the booking.')).toBeInTheDocument();
    expect(confirm).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send payment link' })).toBeDisabled();
    expect(screen.getByLabelText('First name')).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Add manual payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add manual payment' });
    expect(within(dialog).getByText('To confirm this booking, record at least $1,000.00.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Amount')).toHaveValue(1000);
    await user.click(within(dialog).getByRole('button', { name: 'Add' }));
    expect(await screen.findByText('Not saved')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add manual payment' })).toBeDisabled();
    expect(confirm).toBeEnabled();
    expect(writes()).toHaveLength(0);

    let paid: unknown;
    server.use(http.post('*/api/bookings/:id/confirmation', async ({ request }) => {
      paid = await request.json();
      const confirmed = detail({ reference: 'LEE-2610120', createdBy: 'Customer', customer: { ...customer, firstName: 'Ann', lastName: 'Lee', email: 'ann@example.com' } });
      api.booking = confirmed;
      return HttpResponse.json(confirmed);
    }));
    await user.click(confirm);
    expect(await screen.findByText('Booking LEE-2610120 confirmed.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('First name')).toBeEnabled());
    expect(screen.getByLabelText('First name')).toHaveValue('Ann');
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeEnabled();
    expect(paid).toMatchObject({ amount: 1000, date: today, version: 4 });
  });

  it('a confirming payment below the checkout amount is refused in the dialog', async () => {
    api.booking = detail({ ...onlineHold, status: 'Expired', checkoutAmount: 1000 });
    renderAdmin(<BookingDialog bookingId="bk-1" onClose={() => {}} />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Add manual payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add manual payment' });
    fireEvent.change(within(dialog).getByLabelText('Amount'), { target: { value: '500' } });
    await user.click(within(dialog).getByRole('button', { name: 'Add' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('To confirm this booking, record at least $1,000.00.');
    expect(screen.queryByText('Not saved')).not.toBeInTheDocument();
    expect(writes()).toHaveLength(0);
  });

  it('a confirmation on taken dates shows the conflicting references and records nothing', async () => {
    api.booking = detail({ ...onlineHold, status: 'Expired', checkoutAmount: 1000 });
    renderAdmin(<BookingDialog bookingId="bk-1" onClose={() => {}} />);
    const user = userEvent.setup();
    await screen.findByRole('button', { name: 'Confirm booking' });
    await addPayment(user, '1000');
    server.use(http.post('*/api/bookings/:id/confirmation', () =>
      HttpResponse.json({ title: 'Those dates are no longer available.', status: 409, conflictingReferences: ['JONES-2610120'] }, { status: 409 })));
    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));
    expect(await screen.findByText('Those dates are no longer available. (JONES-2610120) Nothing was recorded.')).toBeInTheDocument();
  });

  it('a paid-but-expired booking keeps Add manual payment and Remove disabled', async () => {
    api.booking = detail({
      ...onlineHold, status: 'Expired', reference: 'LEE-2610120', checkoutAmount: 1000, amountPaid: 1000,
      payments: [{ id: 'p', amount: 1000, method: 'Stripe', paidAt: '2026-10-01T03:00:00Z', note: null, recordedBy: 'Customer' }],
    });
    renderAdmin(<BookingDialog bookingId="bk-1" onClose={() => {}} />);
    expect(await screen.findByText('Paid online after the hold expired — refund in Stripe.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add manual payment' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove the $1,000.00 payment of 01/10/2026' })).toBeDisabled();
  });
});

describe('Booking dialog: superseded stand-bys', () => {
  it('shows the banner, locks Status and Stand-by, and reactivates the booking when its dates change', async () => {
    const { onClose, user } = await openExisting({ status: 'Superseded', isStandby: true });
    expect(screen.getByText('This stand-by booking was superseded by another booking. Change its boat or dates to reactivate it as a stand-by booking.'))
      .toBeInTheDocument();
    expect(screen.getByLabelText('Stand-by booking')).toBeDisabled();
    expect(screen.queryByRole('combobox', { name: 'Status' })).not.toBeInTheDocument();
    expect(screen.getByText('Superseded', { selector: 'div' })).toBeInTheDocument();
    server.use(http.put('*/api/bookings/:id', async ({ request }) => {
      api.calls.push({ method: 'PUT', path: '/api/bookings/bk-1', body: await request.json() });
      return HttpResponse.json({ booking: detail({ status: 'Active', isStandby: true }), warnings: [] });
    }));
    fireEvent.change(screen.getByLabelText('Start date'), { target: { value: '2026-10-19' } });
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(screen.getByText('Booking SMITH-2610120 reactivated as a stand-by.')).toBeInTheDocument();
    expect(writes()[0].body).toMatchObject({ status: 'Superseded', isStandby: true, startDate: '2026-10-19', endDate: '2026-10-23' });
  });
});

describe('Booking dialog: check-in and check-out times', () => {
  it.each([['930', '09:30'], ['1730', '17:30'], ['9:30', '09:30'], ['23:59', '23:59']])('formats %s as %s when it loses focus', async (typed, shown) => {
    const { user } = await openExisting();
    const checkIn = screen.getByLabelText('Check-in time');
    expect(checkIn).toHaveAttribute('type', 'text');
    await user.clear(checkIn);
    await user.type(checkIn, typed);
    await user.tab();
    await waitFor(() => expect(checkIn).toHaveValue(shown));
    expect(screen.queryByText('Enter a 24-hour time, e.g. 14:30.')).not.toBeInTheDocument();
  });

  it('shows an error for anything else', async () => {
    const { user } = await openExisting();
    const checkOut = screen.getByLabelText('Check-out time');
    await user.clear(checkOut);
    await user.type(checkOut, '24:00');
    await user.tab();
    expect(await screen.findByText('Enter a 24-hour time, e.g. 14:30.')).toBeInTheDocument();
  });

  it('sends the formatted times', async () => {
    const { onClose, user } = await openExisting();
    const checkIn = screen.getByLabelText('Check-in time');
    await user.clear(checkIn);
    await user.type(checkIn, '1530');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(writes()[0].body).toMatchObject({ checkInTime: '15:30', checkOutTime: '08:00' });
  });
});

describe('Booking dialog: saving', () => {
  it('pre-fills the end date and hire price for a new booking', async () => {
    renderAdmin(<BookingDialog bookingId={null} initial={{ boatId: 'b-pb', day: '2026-10-12' }} onClose={() => {}} />);
    expect(screen.getByLabelText('Period type')).toHaveValue('Midweek'); // a Monday
    expect(screen.getByLabelText('End date')).toHaveValue('2026-10-16');
    await waitFor(() => expect(screen.getByLabelText('Hire price')).toHaveValue(4060));
    await userEvent.setup().selectOptions(screen.getByLabelText('Period type'), 'Week');
    expect(screen.getByLabelText('End date')).toHaveValue('2026-10-19');
  });

  it('validates start < end when either date loses focus and shows it on the end date', async () => {
    const { user } = await openExisting({ periodType: 'Custom' });
    const end = screen.getByLabelText('End date');
    fireEvent.change(end, { target: { value: '2026-10-10' } });
    fireEvent.blur(end);
    expect(await screen.findByText('The end date must be after the start date.')).toBeInTheDocument();
    fireEvent.change(end, { target: { value: '2026-10-20' } });
    await waitFor(() => expect(screen.queryByText('The end date must be after the start date.')).not.toBeInTheDocument());
    await user.click(screen.getByLabelText('Start date'));
    fireEvent.change(screen.getByLabelText('Start date'), { target: { value: '2026-10-25' } }); // Custom keeps a later end date only
    fireEvent.blur(screen.getByLabelText('Start date'));
    expect(screen.getByLabelText('End date')).toHaveValue('2026-10-26');
  });

  it('pressing the main button validates every field and focuses the first invalid one, sending nothing', async () => {
    const { user } = await openExisting();
    await user.clear(screen.getByLabelText('First name'));
    const email = screen.getByLabelText('Email');
    await user.clear(email);
    await user.type(email, 'jane@gmail.c0m');
    fireEvent.change(screen.getByLabelText('Postcode'), { target: { value: '50' } });
    const button = screen.getByRole('button', { name: 'Save changes' });
    expect(button).toBeEnabled();
    await user.click(button);
    expect(await screen.findByText('First name is required.')).toBeInTheDocument();
    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument();
    expect(screen.getByText('Enter a 4-digit postcode.')).toBeInTheDocument();
    expect(email).toHaveFocus();
    expect(writes()).toHaveLength(0);
  });

  it('sends the address with the booking', async () => {
    const { onClose, user } = await openExisting();
    await user.type(screen.getByLabelText('City/Locality'), '  Mannum  ');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(writes()[0].body).toMatchObject({
      address: { line1: null, line2: null, locality: 'Mannum', state: null, postcode: null, country: 'AU' },
    });
  });

  it('locks the form and shows progress while saving, then closes on success', async () => {
    // Hold the save open until released, so the "Saving…" state lasts exactly as long as the test needs
    // (a fixed delay races the assertions below on slower machines, e.g. CI).
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(http.put('*/api/bookings/:id', async ({ request }) => {
      api.calls.push({ method: request.method, path: new URL(request.url).pathname, body: await request.clone().json() });
      await gate;
      return HttpResponse.json({ booking: api.booking, warnings: [] });
    }));
    const { onClose, user } = await openExisting();
    await user.type(screen.getByLabelText('Booking comments'), 'Note');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    const busy = await screen.findByRole('button', { name: 'Saving…' });
    expect(busy).toBeDisabled();
    expect(screen.getByLabelText('Booking comments')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await user.click(busy); // disabled: must not send a second request
    release();
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(writes().filter((c) => c.method === 'PUT')).toHaveLength(1);
    expect(writes()[0].body).toMatchObject({ bookingComments: 'Note', version: 4, addonLines: [{ id: 'l-1', addonId: 'ice', quantity: 2, unitPrice: 5 }] });
  });

  it('stays open with the error and the typed values when saving fails', async () => {
    server.use(http.put('*/api/bookings/:id', async () => {
      await delay(10);
      return HttpResponse.json({ title: 'Those dates are no longer available', status: 409, conflictingReferences: ['LEE-2610140'] }, { status: 409 });
    }));
    const { onClose, user } = await openExisting();
    await user.type(screen.getByLabelText('Booking comments'), 'Keep me');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Those dates are no longer available (LEE-2610140)')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Booking comments')).toHaveValue('Keep me');
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeEnabled();
  });

  it('warns about guests above the berth and beds without blocking', async () => {
    const { user } = await openExisting();
    const adults = screen.getByLabelText('Number of adults');
    await user.clear(adults);
    await user.type(adults, '9');
    expect(screen.getByText(/9 guests is more than Pacific Blue's 8 beds/)).toBeInTheDocument();
    await user.clear(adults);
    await user.type(adults, '13');
    expect(screen.getByText(/13 guests is more than Pacific Blue's berth of 12/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeEnabled();
  });
});
