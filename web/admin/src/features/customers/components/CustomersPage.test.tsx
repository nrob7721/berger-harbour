import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { customer, server } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { CustomersPage } from './CustomersPage';

describe('Customers page', () => {
  it('shows the address read-only at the top of the expander, or "No address"', async () => {
    server.use(
      http.get('*/api/customers', () => HttpResponse.json([
        { ...customer, address: { line1: '1 River Rd', line2: null, locality: 'Mannum', state: 'SA', postcode: '5238', country: 'AU' } },
        { ...customer, id: 'c-2', email: 'bob@example.com', firstName: 'Bob', address: null },
      ])),
      http.get('*/api/customers/:id/bookings', () => HttpResponse.json([])),
    );
    renderAdmin(<CustomersPage onOpenBooking={() => {}} />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Expand Jane Smith' }));
    const address = await screen.findByTestId('customer-address');
    expect(address.textContent).toBe('1 River Rd\nMannum SA 5238\nAustralia'); // blank parts skipped
    expect(screen.queryByRole('textbox', { name: 'Address' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Expand Bob Smith' }));
    expect(await screen.findByTestId('customer-address')).toHaveTextContent('No address');
  });
});
