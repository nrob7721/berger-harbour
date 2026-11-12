import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { apiState, details, server } from '@/testing/server';
import { currentState, filledState, renderWithBooking } from '@/testing/render';
import { AddonsStep } from './AddonsStep';

const itemFor = (name: string) => screen.getByText(name).closest('li')!;

describe('Add-ons step', () => {
  it('shows the remaining capacity and caps the quantity', async () => {
    renderWithBooking(<AddonsStep />, filledState);
    expect(await screen.findByText('6 car spots remaining')).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(within(itemFor('Car parking')).getByRole('checkbox'));
    const qty = within(itemFor('Car parking')).getByRole('spinbutton');
    expect(qty).toHaveAttribute('max', '6');
    await user.clear(qty);
    await user.type(qty, '9');
    expect(currentState().addons['car-parking']).toBe(6);
  });

  it('always shows the remaining figure with the unit label', async () => {
    renderWithBooking(<AddonsStep />, filledState);
    expect(await screen.findByText('6 car spots remaining')).toBeInTheDocument();
    expect(screen.queryByText(/available for your dates/)).not.toBeInTheDocument();
  });

  it('hides a capacity add-on when none remain', async () => {
    apiState.remaining = 0;
    renderWithBooking(<AddonsStep />, { ...filledState, addons: { 'car-parking': 2 } });
    await screen.findByText('Ice');
    await waitFor(() => expect(screen.queryByText('Car parking')).not.toBeInTheDocument());
    await waitFor(() => expect(currentState().addons).toEqual({}));
  });

  it('fetches remaining capacity every time the step is shown', async () => {
    const first = renderWithBooking(<AddonsStep />, filledState);
    await screen.findByText('6 car spots remaining');
    first.unmount();
    apiState.remaining = 3;
    renderWithBooking(<AddonsStep />, filledState, first.client);
    expect(await screen.findByText('3 car spots remaining')).toBeInTheDocument();
    expect(apiState.calls.filter((c) => c.path.includes('/api/addons/car-parking/availability'))).toHaveLength(2);
  });

  it('updates the booking price as add-ons are ticked and quantities change', async () => {
    renderWithBooking(<AddonsStep />, filledState);
    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByTestId('booking-price')).toHaveTextContent('$4,060.00'));
    await user.click(within(itemFor('Outboard motor')).getByRole('checkbox'));
    expect(screen.getByTestId('booking-price')).toHaveTextContent('$4,155.00');
    await user.click(within(itemFor('Ice')).getByRole('checkbox'));
    fireEvent.change(within(itemFor('Ice')).getByRole('spinbutton'), { target: { value: '4' } });
    expect(screen.getByTestId('booking-price')).toHaveTextContent('$4,175.00');
    expect(screen.queryByText('Excludes price-on-request add-ons')).not.toBeInTheDocument();
    await user.click(within(itemFor('Welcome hamper')).getByRole('checkbox'));
    expect(screen.getByText('Excludes price-on-request add-ons')).toBeInTheDocument();
    expect(within(itemFor('Welcome hamper')).getByText('Price on request')).toBeInTheDocument();
  });

  it('removes add-ons the newly selected boat does not offer', async () => {
    apiState.boatDetails['pacific-blue'] = details('pacific-blue', { addons: details('pacific-blue').addons.filter((a) => a.id !== 'outboard-motor') });
    renderWithBooking(<AddonsStep />, { ...filledState, addons: { 'outboard-motor': 1, ice: 2 } });
    expect(await screen.findByText("Some add-ons you chose aren't offered on Pacific Blue and have been removed.")).toBeInTheDocument();
    expect(currentState().addons).toEqual({ ice: 2 });
  });

  it('saves the draft and moves on with Continue', async () => {
    let body: unknown;
    server.use(http.put('*/api/booking-drafts/:id', async ({ request }) => {
      body = await request.json();
      return new HttpResponse(null, { status: 204 });
    }));
    renderWithBooking(<AddonsStep />, { ...filledState, addons: { ice: 2 } });
    const user = userEvent.setup();
    await screen.findByText('6 car spots remaining');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(currentState().step).toBe('summary'));
    expect(body).toMatchObject({ lastStepReached: 'Summary', addons: [{ addonId: 'ice', quantity: 2 }] });
  });
});
