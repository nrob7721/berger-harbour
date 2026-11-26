import { describe, expect, it } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import userEvent from '@testing-library/user-event';
import { api, boats, server } from '@/testing/server';
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
    const user = userEvent.setup();
    await user.click(screen.getByRole('tab', { name: 'Photos' }));
    const url = screen.getAllByLabelText('Image URL')[1];
    await user.clear(url);
    await user.type(url, 'https://evil.example.com/a.jpg');
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

describe('Boat dialog tags', () => {
  const tagInput = () => screen.getByLabelText('Tags');
  const chips = () => within(screen.getByRole('list', { name: 'Current tags' })).getAllByRole('listitem').map((c) => c.firstChild?.textContent);

  it('adds the typed tag as a chip at the end with Enter, without submitting the dialog', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    expect(chips()).toEqual(['Sleeps 12', 'Heated spa']);
    await user.type(tagInput(), '  Air conditioning {Enter}');
    expect(chips()).toEqual(['Sleeps 12', 'Heated spa', 'Air conditioning']);
    expect(tagInput()).toHaveValue('');
    await user.type(tagInput(), '   {Enter}'); // a blank entry adds nothing
    expect(chips()).toHaveLength(3);
    expect(writes()).toHaveLength(0);
  });

  it('removes a tag with its ×, and never with Backspace in the empty input', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.click(tagInput());
    await user.keyboard('{Backspace}');
    expect(chips()).toEqual(['Sleeps 12', 'Heated spa']);
    await user.click(screen.getByRole('button', { name: 'Remove Sleeps 12' }));
    expect(chips()).toEqual(['Heated spa']);
  });

  it('keeps an over-long or duplicate tag in the input with its error', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(tagInput(), 'HEATED SPA{Enter}');
    expect(screen.getByText('This tag is already on the boat.')).toBeInTheDocument();
    expect(tagInput()).toHaveValue('HEATED SPA');
    await user.clear(tagInput());
    expect(screen.queryByText('This tag is already on the boat.')).not.toBeInTheDocument();
    await user.type(tagInput(), `${'x'.repeat(61)}{Enter}`);
    expect(screen.getByText('Keep each tag to 60 characters.')).toBeInTheDocument();
    expect(tagInput()).toHaveValue('x'.repeat(61));
    expect(chips()).toEqual(['Sleeps 12', 'Heated spa']);
  });
});

describe('Boat dialog photos', () => {
  const rows = () => screen.getAllByTestId('photo-row');
  const urlsShown = () => rows().map((r) => (within(r).getByLabelText('Image URL') as HTMLInputElement).value);
  const uploads = 'https://bergerhouseboats.com.au/wp-content/uploads/';

  it('labels the first row as the thumbnail image, moves rows with ▲/▼ and adds a row at the end', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('tab', { name: 'Photos' }));
    expect(urlsShown()).toEqual([`${uploads}pb-1.jpg`, `${uploads}pb-2.jpg`]);
    expect(within(rows()[0]).getByText('Thumbnail image')).toBeInTheDocument();
    expect(within(rows()[1]).queryByText('Thumbnail image')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move photo 1 up' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Move photo 2 up' }));
    expect(urlsShown()).toEqual([`${uploads}pb-2.jpg`, `${uploads}pb-1.jpg`]);
    expect((within(rows()[0]).getByLabelText('Description') as HTMLInputElement).value).toBe('Upper deck');
    await user.click(screen.getByRole('button', { name: 'Move photo 1 down' }));
    expect(urlsShown()).toEqual([`${uploads}pb-1.jpg`, `${uploads}pb-2.jpg`]);
    await user.click(screen.getByRole('button', { name: '+ Add photo' }));
    expect(urlsShown()).toEqual([`${uploads}pb-1.jpg`, `${uploads}pb-2.jpg`, '']);
    await user.click(screen.getByRole('button', { name: 'Remove photo 3' }));
    expect(rows()).toHaveLength(2);
    expect(writes()).toHaveLength(0); // nothing is saved until Save
  });

  it('saves the photos and tags in their order with the boat', async () => {
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Tags'), 'Generator{Enter}');
    await user.click(screen.getByRole('tab', { name: 'Photos' }));
    await user.click(screen.getByRole('button', { name: 'Move photo 2 up' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0].body).toMatchObject({
      images: [{ url: `${uploads}pb-2.jpg`, description: 'Upper deck' }, { url: `${uploads}pb-1.jpg`, description: null }],
      tags: ['Sleeps 12', 'Heated spa', 'Generator'],
    });
  });

  it('shows a server error on the photo concerned, on the Photos tab', async () => {
    server.use(http.put('*/api/boats/:id', () => HttpResponse.json(
      { title: 'One or more validation errors occurred.', status: 400, errors: { 'images[1].description': ['Keep the description to 120 characters.'] } },
      { status: 400 },
    )));
    renderAdmin(<BoatDialog boat={boats[0]} onClose={() => {}} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await within(rows()[1]).findByText('Keep the description to 120 characters.')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Photos' })).toHaveAttribute('aria-selected', 'true');
  });
});
