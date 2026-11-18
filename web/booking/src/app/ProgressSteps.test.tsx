import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { currentState, filledState, renderWithBooking, renderWithClient } from '@/testing/render';
import { apiState, boats, details, server, settings } from '@/testing/server';
import type { BookingState } from '@/lib/booking-state';
import type { Quote } from '@/lib/types';
import { BookingApp, Steps } from './BookingApp';

const quote: Quote = {
  boatName: 'Pacific Blue', periodType: 'Midweek', startDate: '2026-10-12', endDate: '2026-10-16', numberOfAdults: 2, numberOfChildren: 0,
  seasonName: 'Normal', hirePrice: 4060, addons: [], totalPrice: 4060, hasPriceOnRequestAddons: false, depositAmount: 1000, amountDueAtCheckout: 1000,
  paymentSchedule: [{ kind: 'Deposit', label: 'Deposit', requiredCumulative: 1000, amount: 1000, dueDate: '2026-08-01', dueAtBooking: true }],
  securityBond: 2000, roomingWarning: null,
};

const progress = () => screen.getByRole('list', { name: 'Booking progress' });
const stepButton = (name: string) => within(progress()).getByRole('button', { name });
const draftUpdates = () => apiState.calls.filter((c) => c.method === 'PUT' && c.path.startsWith('/api/booking-drafts/'));

function renderSteps(state: Partial<BookingState>) {
  server.use(http.post('*/api/boats/:slug/quote', () => HttpResponse.json(quote)));
  return renderWithBooking(<Steps fleet={{ boats, settings }} requested={null} />, state);
}

describe('Booking app: page heading', () => {
  it('shows no page title above the progress steps; the step heading is the only main heading', async () => {
    renderWithClient(<BookingApp requestedSlug="pacific-blue" returnedBooking={null} />);
    expect(await screen.findByRole('heading', { level: 2, name: 'Choose your dates and boat' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
    expect(screen.queryByText('Book Pacific Blue')).not.toBeInTheDocument();
  });
});

describe('Progress steps', () => {
  it('make each step up to the furthest reached a button, except the current step, Payment and Complete', () => {
    renderSteps({ ...filledState, step: 'details', furthestStep: 'summary' });
    const buttons = within(progress()).getAllByRole('button').map((b) => b.textContent);
    expect(buttons).toEqual(['1. Dates & boat', '3. Add-ons', '4. Summary']);
    expect(within(progress()).getByText('2. Your details').closest('li')).toHaveAttribute('aria-current', 'step');
    expect(within(progress()).getByText('5. Payment').closest('button')).toBeNull();
  });

  it('later steps not reached yet are not buttons', () => {
    renderSteps({ ...filledState, step: 'details', furthestStep: 'details' });
    expect(within(progress()).getAllByRole('button').map((b) => b.textContent)).toEqual(['1. Dates & boat']);
  });

  it('going back to an earlier step opens it straight away, keeping everything entered', async () => {
    renderSteps({ ...filledState, step: 'addons', furthestStep: 'summary', addons: { ice: 2 } });
    await userEvent.setup().click(stepButton('1. Dates & boat'));
    expect(currentState().step).toBe('dates');
    expect(currentState()).toMatchObject({ addons: { ice: 2 }, selectedSlug: 'pacific-blue', period: filledState.period, furthestStep: 'summary' });
    expect(apiState.calls.filter((c) => c.method !== 'GET')).toHaveLength(0);
  });

  it('leaving the Payment step through a progress step releases the hold first', async () => {
    const hold = { bookingId: 'b-1', clientSecret: 'cs_1_secret', sessionId: 'cs_1', checkoutExpiresAt: new Date(Date.now() + 1_800_000).toISOString() };
    renderSteps({ ...filledState, step: 'payment', furthestStep: 'payment', hold });
    await userEvent.setup().click(stepButton('2. Your details'));
    await waitFor(() => expect(currentState().step).toBe('details'));
    expect(currentState().hold).toBeNull();
    expect(apiState.calls.some((c) => c.method === 'POST' && c.path === '/api/bookings/b-1/release')).toBe(true);
  });

  it('a release 409 on the way out of Payment goes to Complete', async () => {
    apiState.releaseStatus = 409;
    const hold = { bookingId: 'b-1', clientSecret: 'cs_1_secret', sessionId: 'cs_1', checkoutExpiresAt: new Date(Date.now() + 1_800_000).toISOString() };
    renderSteps({ ...filledState, step: 'payment', furthestStep: 'payment', hold });
    await userEvent.setup().click(stepButton('1. Dates & boat'));
    await waitFor(() => expect(currentState().step).toBe('complete'));
  });

  it('a forward jump runs each step\'s Continue on the way and opens the clicked step', async () => {
    renderSteps({ ...filledState, step: 'dates', furthestStep: 'summary', addons: { ice: 1 } });
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    await userEvent.setup().click(stepButton('4. Summary'));
    await waitFor(() => expect(currentState().step).toBe('summary'));
    expect(draftUpdates().map((c) => (c.body as { lastStepReached: string }).lastStepReached)).toEqual(['Addons', 'Summary']);
    expect(currentState().jumpTarget).toBeNull();
  });

  it('a forward jump from Your details validates and saves it first, as Continue does', async () => {
    renderSteps({ ...filledState, step: 'details', furthestStep: 'summary' });
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('First name'));
    await user.click(stepButton('3. Add-ons'));
    expect(await screen.findByText('First name is required.')).toBeInTheDocument();
    expect(currentState().step).toBe('details');
    expect(draftUpdates()).toHaveLength(0);
    await user.type(screen.getByLabelText('First name'), 'Janet');
    await user.click(stepButton('3. Add-ons'));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(currentState().details?.firstName).toBe('Janet');
  });

  it('edits on Your details are kept, unsaved, when leaving it for an earlier step, then saved by the next Continue', async () => {
    renderSteps({ ...filledState, step: 'summary', furthestStep: 'summary' });
    const user = userEvent.setup();
    await user.click(stepButton('2. Your details'));
    await user.clear(screen.getByLabelText('First name'));
    await user.type(screen.getByLabelText('First name'), 'Janet');
    await user.click(stepButton('1. Dates & boat'));
    expect(currentState().step).toBe('dates');
    expect(draftUpdates()).toHaveLength(0);
    await user.click(stepButton('2. Your details'));
    expect(screen.getByLabelText('First name')).toHaveValue('Janet');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(draftUpdates().map((c) => (c.body as { firstName: string }).firstName)).toEqual(['Janet']);
  });

  it('Back keeps the edits on Your details too, even invalid, without validating or saving them', async () => {
    renderSteps({ ...filledState, step: 'details', furthestStep: 'summary' });
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('First name'));
    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(currentState().step).toBe('dates');
    expect(currentState().details?.firstName).toBe('');
    expect(draftUpdates()).toHaveLength(0);
    await user.click(stepButton('2. Your details'));
    expect(screen.getByLabelText('First name')).toHaveValue('');
    expect(screen.queryByText('First name is required.')).not.toBeInTheDocument();
  });

  it('a forward jump from Dates & boat saves the details kept from Your details', async () => {
    renderSteps({ ...filledState, step: 'details', furthestStep: 'summary' });
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('Last name'));
    await user.type(screen.getByLabelText('Last name'), 'Jones');
    await user.click(stepButton('1. Dates & boat'));
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    await user.click(stepButton('4. Summary'));
    await waitFor(() => expect(currentState().step).toBe('summary'));
    expect(draftUpdates().map((c) => (c.body as { lastName: string }).lastName)).toEqual(['Jones', 'Jones']);
  });

  it('stops on Dates & boat with the rooming warning open', async () => {
    renderSteps({ ...filledState, adults: 9, step: 'dates', furthestStep: 'summary' }); // Pacific Blue has 8 beds
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    const user = userEvent.setup();
    await user.click(stepButton('4. Summary'));
    const dialog = await screen.findByRole('dialog', { name: 'Sleeping arrangements' });
    expect(currentState().step).toBe('dates');
    expect(currentState().jumpTarget).toBeNull();
    await user.click(within(dialog).getByRole('button', { name: 'Accept' }));
    await waitFor(() => expect(currentState().step).toBe('details')); // Accept continues to the next step as usual
  });

  it('stops on Dates & boat when the period or boat is no longer chosen', async () => {
    renderSteps({ ...filledState, selectedSlug: null, step: 'dates', furthestStep: 'summary' });
    await userEvent.setup().click(stepButton('4. Summary'));
    expect(await screen.findByText('Choose your dates and a boat to continue.')).toBeInTheDocument();
    expect(currentState().step).toBe('dates');
  });

  it('stops on Your details, showing its errors, when the details are invalid', async () => {
    renderSteps({ ...filledState, details: { ...filledState.details!, email: 'jane@gmail' }, step: 'dates', furthestStep: 'summary' });
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    await userEvent.setup().click(stepButton('4. Summary'));
    await waitFor(() => expect(currentState().step).toBe('details'));
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
  });

  it('stops on Add-ons when add-ons were removed because the boat changed', async () => {
    apiState.boatDetails['pacific-blue'] = details('pacific-blue', { addons: [] });
    renderSteps({ ...filledState, step: 'dates', furthestStep: 'summary', addons: { ice: 2 } });
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    await userEvent.setup().click(stepButton('4. Summary'));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(currentState().addons).toEqual({});
    expect(screen.getByText("Some add-ons you chose aren't offered on Pacific Blue and have been removed.")).toBeInTheDocument();
  });

  it('stops on Add-ons when a capacity-limited add-on is above what now remains', async () => {
    apiState.remaining = 1;
    renderSteps({ ...filledState, step: 'dates', furthestStep: 'summary', addons: { 'car-parking': 3 } });
    await waitFor(() => expect(within(progress()).getAllByRole('button')).toHaveLength(3));
    await userEvent.setup().click(stepButton('4. Summary'));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(screen.getByText('Car parking: only 1 car spots remain for your dates.')).toBeInTheDocument();
    expect(await screen.findByText('1 car spots remaining')).toBeInTheDocument();
  });
});
