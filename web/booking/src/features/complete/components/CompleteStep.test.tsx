import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '@/testing/server';
import { renderWithBooking } from '@/testing/render';
import { closeBooking, CLOSE_MESSAGE } from '../utils/close';
import { CompleteStep } from './CompleteStep';

describe('Complete step', () => {
  it('shows the booking reference once the booking is active', async () => {
    let calls = 0;
    server.use(http.get('*/api/bookings/:id/status', () => {
      calls++;
      return HttpResponse.json({
        status: calls < 2 ? 'PendingPayment' : 'Active', reference: calls < 2 ? null : 'SMITH-2610120', boatName: 'Pacific Blue',
        startDate: '2026-10-12', endDate: '2026-10-16', email: 'jane@example.com',
      });
    }));
    renderWithBooking(<CompleteStep />, { step: 'complete', completed: { bookingId: 'b-1', sessionId: 'cs_1', email: 'jane@example.com' } });
    expect(await screen.findByText(/Confirming/)).toBeInTheDocument();
    expect(await screen.findByTestId('booking-reference', {}, { timeout: 4000 })).toHaveTextContent('SMITH-2610120');
    expect(screen.getByText('A confirmation email has been sent to jane@example.com')).toBeInTheDocument();
  });
});

describe('Close', () => {
  it('posts bh-booking:close to the WordPress page inside an iframe', () => {
    const parent = { postMessage: vi.fn() };
    const win = { parent, close: vi.fn(), setTimeout: vi.fn() } as unknown as Window;
    closeBooking('pacific-blue', win);
    expect(parent.postMessage).toHaveBeenCalledWith(CLOSE_MESSAGE, 'https://bergerhouseboats.com.au');
    expect(parent.postMessage).toHaveBeenCalledWith(CLOSE_MESSAGE, 'https://www.bergerhouseboats.com.au');
    expect((win as unknown as { close: ReturnType<typeof vi.fn> }).close).not.toHaveBeenCalled();
  });

  it('closes the tab, or goes to the boat page when the window stays open', () => {
    const assign = vi.fn();
    const win = {
      closed: false, close: vi.fn(), location: { assign }, setTimeout: (fn: () => void) => fn(),
    } as unknown as Window & { parent: Window };
    (win as { parent: Window }).parent = win;
    closeBooking('pacific-blue', win);
    expect(win.close).toHaveBeenCalled();
    expect(assign).toHaveBeenCalledWith('https://bergerhouseboats.com.au/houseboats/pacific-blue/');
    closeBooking(null, win);
    expect(assign).toHaveBeenLastCalledWith('https://bergerhouseboats.com.au/');
  });
});
