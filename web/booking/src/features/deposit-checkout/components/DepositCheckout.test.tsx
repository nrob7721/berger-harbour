import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, delay } from 'msw';
import { server } from '@/testing/server';
import { currentState, filledState, renderWithBooking } from '@/testing/render';
import { minutesLeft, PayButton, PaymentStep } from './DepositCheckout';

const inMinutes = (m: number) => new Date(Date.now() + m * 60_000).toISOString();
const hold = { bookingId: 'b-1', clientSecret: 'cs_1_secret', sessionId: 'cs_1', checkoutExpiresAt: inMinutes(30) };

describe('Payment step', () => {
  it('Back releases the hold and returns to the Summary', async () => {
    let released: unknown;
    server.use(http.post('*/api/bookings/:id/release', async ({ request }) => {
      released = await request.json();
      await delay(20);
      return new HttpResponse(null, { status: 204 });
    }));
    renderWithBooking(<PaymentStep />, { ...filledState, step: 'payment', hold });
    expect(screen.getByTestId('embedded-checkout')).toHaveAttribute('data-secret', 'cs_1_secret');
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('button', { name: 'Going back…' })).toBeDisabled();
    await waitFor(() => expect(currentState().step).toBe('summary'));
    expect(currentState().hold).toBeNull();
    expect(released).toEqual({ draftId: 'draft-1' });
  });

  it('goes to Complete when the release finds the payment already done (409)', async () => {
    server.use(http.post('*/api/bookings/:id/release', () =>
      HttpResponse.json({ title: 'Your payment has already been completed.', status: 409 }, { status: 409 })));
    renderWithBooking(<PaymentStep />, { ...filledState, step: 'payment', hold });
    await userEvent.setup().click(screen.getByRole('button', { name: 'Back' }));
    await waitFor(() => expect(currentState().step).toBe('complete'));
    expect(currentState().completed).toEqual({ bookingId: 'b-1', sessionId: 'cs_1', email: 'jane@example.com' });
  });

  it('completing Checkout moves to Complete', async () => {
    renderWithBooking(<PaymentStep />, { ...filledState, step: 'payment', hold });
    await userEvent.setup().click(screen.getByRole('button', { name: 'Simulate payment' }));
    expect(currentState().step).toBe('complete');
  });
});

describe('Payment step: hold countdown', () => {
  afterEach(() => vi.useRealTimers());

  it('counts whole minutes rounded up', () => {
    const now = Date.parse('2026-10-01T00:00:00Z');
    expect(minutesLeft('2026-10-01T00:30:00Z', now)).toBe(30);
    expect(minutesLeft('2026-10-01T00:29:00.001Z', now)).toBe(30);
    expect(minutesLeft('2026-10-01T00:00:01Z', now)).toBe(1);
    expect(minutesLeft('2026-10-01T00:00:00Z', now)).toBe(0);
  });

  it('updates as time passes, says "1 minute" in the last minute, then shows that the dates are no longer held', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    vi.setSystemTime(new Date('2026-10-01T00:00:00Z'));
    renderWithBooking(<PaymentStep />, { ...filledState, step: 'payment', hold: { ...hold, checkoutExpiresAt: '2026-10-01T00:01:30Z' } });
    expect(screen.getByText('Dates are guaranteed for the next 2 minutes')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(31_000));
    expect(screen.getByText('Dates are guaranteed for the next 1 minute')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(60_000));
    expect(screen.getByText('Your dates are no longer held. Go back to try again.')).toBeInTheDocument();
    expect(screen.queryByTestId('embedded-checkout')).not.toBeInTheDocument();
    vi.useRealTimers();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Back' }));
    await waitFor(() => expect(currentState().step).toBe('summary'));
    expect(currentState().hold).toBeNull();
  });
});

describe('Pay button', () => {
  it('creates the hold and moves to Payment', async () => {
    server.use(http.post('*/api/bookings', async () => {
      await delay(20);
      return HttpResponse.json(hold);
    }));
    renderWithBooking(<PayButton amount={1000} termsAccepted />, { ...filledState, step: 'summary' });
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Pay $1,000.00' }));
    expect(await screen.findByRole('button', { name: 'Starting payment…' })).toBeDisabled();
    await waitFor(() => expect(currentState().step).toBe('payment'));
    expect(currentState().hold).toEqual(hold);
  });

  it('is disabled until the terms are accepted', () => {
    renderWithBooking(<PayButton amount={1000} termsAccepted={false} />, { ...filledState, step: 'summary' });
    expect(screen.getByRole('button', { name: 'Pay $1,000.00' })).toBeDisabled();
  });

  it('returns to Dates & boat when the dates were just taken, keeping the period and deselecting the boat', async () => {
    server.use(http.post('*/api/bookings', () => HttpResponse.json({ title: 'Those dates are no longer available', status: 409 }, { status: 409 })));
    renderWithBooking(<PayButton amount={1000} termsAccepted />, { ...filledState, step: 'summary' });
    await userEvent.setup().click(screen.getByRole('button', { name: 'Pay $1,000.00' }));
    await waitFor(() => expect(currentState().step).toBe('dates'));
    expect(currentState().notice).toBe('Sorry, those dates were just taken.');
    expect(currentState().selectedSlug).toBeNull();
    expect(currentState().period).toEqual(filledState.period);
  });

  it('returns to Add-ons with the capacity message', async () => {
    server.use(http.post('*/api/bookings', () =>
      HttpResponse.json({ title: 'x', status: 400, errors: { addons: ['Only 2 car spots remain for your dates.'] } }, { status: 400 })));
    renderWithBooking(<PayButton amount={1000} termsAccepted />, { ...filledState, step: 'summary' });
    await userEvent.setup().click(screen.getByRole('button', { name: 'Pay $1,000.00' }));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(currentState().notice).toBe('Only 2 car spots remain for your dates.');
  });
});
