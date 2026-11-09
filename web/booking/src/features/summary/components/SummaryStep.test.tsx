import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '@/testing/server';
import { filledState, renderWithBooking } from '@/testing/render';
import type { Quote } from '@/lib/types';
import { checkInRow, checkOutRow } from '../utils/rows';
import { SummaryStep } from './SummaryStep';

const quote: Quote = {
  boatName: 'Pacific Blue', periodType: 'Weekend', startDate: '2027-04-02', endDate: '2027-04-05', numberOfAdults: 2, numberOfChildren: 1,
  seasonName: 'Normal', hirePrice: 4060, addons: [{ addonId: 'welcome-hamper', name: 'Welcome hamper', quantity: 1, unitPrice: null, lineTotal: null }],
  totalPrice: 4060, hasPriceOnRequestAddons: true, depositAmount: 1000, amountDueAtCheckout: 1000,
  paymentSchedule: [
    { kind: 'Deposit', label: 'Deposit', requiredCumulative: 1000, amount: 1000, dueDate: '2026-11-09', dueAtBooking: true },
    { kind: 'Balance', label: 'Balance', requiredCumulative: 4060, amount: 3060, dueDate: '2027-03-03', dueAtBooking: false },
  ],
  securityBond: 2000, roomingWarning: null,
};

describe('Summary', () => {
  it('gives check-in and check-out their own zone labels', () => {
    expect(checkInRow('2026-10-16')).toBe('Fri 16/10/2026, 11am – 2pm AEDT/NSW');
    expect(checkOutRow('2026-10-23')).toBe('Fri 23/10/2026, from 8am AEDT/NSW');
    expect(checkInRow('2027-04-02')).toBe('Fri 02/04/2027, 11am – 2pm AEDT/NSW');
    expect(checkOutRow('2027-04-05')).toBe('Mon 05/04/2027, from 8am AEST/NSW');
  });

  it('shows the server quote and enables Pay once the terms are accepted', async () => {
    server.use(http.post('*/api/boats/:slug/quote', () => HttpResponse.json(quote)));
    renderWithBooking(
      <SummaryStep hireTermsUrl="https://example.com/terms" renderPay={({ quote: q, termsAccepted }) => <button disabled={!termsAccepted}>Pay {q.amountDueAtCheckout}</button>} />,
      filledState,
    );
    expect(await screen.findByTestId('summary-check-in')).toHaveTextContent('Fri 02/04/2027, 11am – 2pm AEDT/NSW');
    expect(screen.getByTestId('summary-check-out')).toHaveTextContent('Mon 05/04/2027, from 8am AEST/NSW');
    expect(screen.getByText('Welcome hamper – price on request')).toBeInTheDocument();
    expect(screen.getByText('A $2,000.00 security bond applies — details will be provided.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Hire Terms and Procedures' })).toHaveAttribute('target', '_blank');
    expect(screen.getByRole('button', { name: 'Pay 1000' })).toBeDisabled();
    await userEvent.setup().click(screen.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Pay 1000' })).toBeEnabled();
  });
});
