import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { BlockedPeriodsSection } from './BlockedPeriodsSection';
import { GeneralSection } from './GeneralSection';

// Field validation on blur (§6.1).
describe('General settings validation', () => {
  it('reports an invalid email and BSB as soon as the field loses focus', async () => {
    renderAdmin(<GeneralSection />);
    const user = userEvent.setup();
    const staff = await screen.findByLabelText('Staff alert email');
    await waitFor(() => expect(screen.getByLabelText('Default deposit')).toHaveValue(1000));
    await user.type(staff, 'staff@bergerhouseboats');
    await user.tab();
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('BSB'), '12-34');
    await user.tab();
    expect(await screen.findByText('BSB must be 6 digits, e.g. 123-456.')).toBeInTheDocument();
  });
});

describe('Blocked period validation', () => {
  it('checks first night ≤ last night when either loses focus, showing it on Last night', async () => {
    server.use(http.get('*/api/settings/blocked-periods', () => HttpResponse.json([])));
    renderAdmin(<BlockedPeriodsSection />);
    await userEvent.setup().click(screen.getByRole('button', { name: '+ New blocked period' }));
    const last = screen.getByLabelText('Last night');
    fireEvent.change(last, { target: { value: '2027-03-01' } });
    fireEvent.blur(last);
    const first = screen.getByLabelText('First night');
    fireEvent.change(first, { target: { value: '2027-03-05' } });
    fireEvent.blur(first);
    expect(await screen.findByText("The last night can't be before the first night.")).toBeInTheDocument();
  });
});
