import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api, boats } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { BoatDialog } from './BoatDialog';

const writes = () => api.calls.filter((c) => c.method !== 'GET');

// Field validation on blur (§6.1).
describe('Boat dialog validation', () => {
  it('checks Berth ≥ adult + kids beds when either field loses focus, showing it on Berth', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />); // berth 12
    const kids = screen.getByLabelText('Number of kids beds');
    fireEvent.change(kids, { target: { value: '9' } });
    fireEvent.blur(kids);
    expect(await screen.findByText(/Berth can't be less than adult beds \+ kids beds/)).toBeInTheDocument();
    fireEvent.change(kids, { target: { value: '2' } });
    fireEvent.blur(kids);
    await waitFor(() => expect(screen.queryByText(/Berth can't be less than/)).not.toBeInTheDocument());
  });

  it('reports an invalid image URL on blur', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const url = screen.getByLabelText('Image URL');
    await userEvent.setup().type(url, 'https://evil.example.com/a.jpg');
    fireEvent.blur(url);
    expect(await screen.findByText('Use an https:// link to an image on bergerhouseboats.com.au.')).toBeInTheDocument();
  });

  it('on save, shows the tab holding the first invalid field and focuses it', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('Name'));
    await user.click(screen.getByRole('tab', { name: 'Rates' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveFocus());
    expect(writes()).toHaveLength(0);
  });
});
