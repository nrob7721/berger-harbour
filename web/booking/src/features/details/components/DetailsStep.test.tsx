import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, delay } from 'msw';
import { apiState, server } from '@/testing/server';
import { currentState, renderWithBooking } from '@/testing/render';
import { DetailsStep } from './DetailsStep';

const base = { period: { start: '2026-10-12', end: '2026-10-16' }, selectedSlug: 'pacific-blue', adults: 2, children: 0 } as const;

async function fill(user: ReturnType<typeof userEvent.setup>, mobile = '0412 345 678') {
  await user.type(screen.getByLabelText('First name'), 'Jane');
  await user.type(screen.getByLabelText('Last name'), 'Smith');
  await user.type(screen.getByLabelText('Email'), 'jane@example.com');
  await user.type(screen.getByLabelText('Mobile'), mobile);
}

describe('Your details', () => {
  it('rejects a landline with the mobile message', async () => {
    renderWithBooking(<DetailsStep />, { ...base, draftId: null });
    const user = userEvent.setup();
    await fill(user, '02 9876 5432');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('Enter a valid mobile number for the selected country.')).toBeInTheDocument();
    expect(apiState.calls.some((c) => c.method === 'POST')).toBe(false);
  });

  it('creates the draft once, showing a busy button that cannot be submitted twice', async () => {
    // Hold the response until the busy button has been clicked, so it cannot land mid-click on a slow machine.
    let release!: () => void;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(http.post('*/api/booking-drafts', async ({ request }) => {
      apiState.calls.push({ method: request.method, path: new URL(request.url).pathname, body: await request.clone().json() });
      await gate;
      return HttpResponse.json({ draftId: 'draft-1' });
    }));
    renderWithBooking(<DetailsStep />, { ...base, draftId: null });
    const user = userEvent.setup();
    await fill(user);
    const button = screen.getByRole('button', { name: 'Continue' });
    await waitFor(() => expect(button).toBeEnabled()); // Turnstile solved
    await user.click(button);
    const busy = await screen.findByRole('button', { name: 'Saving…' });
    expect(busy).toBeDisabled();
    await user.click(busy);
    expect(currentState().step).toBe('dates');
    release();
    await waitFor(() => expect(currentState().step).toBe('addons'));
    const posts = apiState.calls.filter((c) => c.method === 'POST' && c.path === '/api/booking-drafts');
    expect(posts).toHaveLength(1);
    expect(posts[0].body).toMatchObject({ mobile: '+61412345678', turnstileToken: 'turnstile-ok', email: 'jane@example.com' });
    expect(currentState().draftId).toBe('draft-1');
    const saved = window.localStorage.getItem('bh-booking-progress')!;
    expect(JSON.parse(saved).draftId).toBe('draft-1');
    for (const personal of ['Jane', 'Smith', 'jane@example.com', '412345678']) expect(saved).not.toContain(personal);
  });

  it('keeps what was typed and shows server field errors on failure', async () => {
    server.use(http.post('*/api/booking-drafts', async () => {
      await delay(10);
      return HttpResponse.json({ title: 'Enter a valid email address.', status: 400, errors: { email: ['That email looks wrong.'] } }, { status: 400 });
    }));
    renderWithBooking(<DetailsStep />, { ...base, draftId: null });
    const user = userEvent.setup();
    await fill(user);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('That email looks wrong.')).toBeInTheDocument();
    expect(screen.getByLabelText('First name')).toHaveValue('Jane');
    expect(currentState().step).toBe('dates');
  });

  it('validates each field when it loses focus and clears the error once fixed', async () => {
    renderWithBooking(<DetailsStep />, { ...base, draftId: null });
    const user = userEvent.setup();
    const email = screen.getByLabelText('Email');
    await user.type(email, 'jane@gmail');
    expect(screen.queryByText('Enter a valid email address.')).not.toBeInTheDocument(); // not while typing
    await user.tab();
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
    await user.type(email, '.com');
    await waitFor(() => expect(screen.queryByText('Enter a valid email address.')).not.toBeInTheDocument());

    await user.click(screen.getByLabelText('First name'));
    await user.tab();
    expect(await screen.findByText('First name is required.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Mobile'), '02 9876 5432');
    await user.tab();
    expect(await screen.findByText('Enter a valid mobile number for the selected country.')).toBeInTheDocument();
    expect(apiState.calls.some((c) => c.method === 'POST')).toBe(false);
  });

  it('re-validates an entered mobile when the country changes', async () => {
    renderWithBooking(<DetailsStep />, { ...base, draftId: null });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Mobile'), '0412 345 678');
    await user.click(screen.getByRole('button', { name: /Country code/ }));
    await user.type(screen.getByLabelText('Search countries'), 'United States');
    await user.click(screen.getByRole('option', { name: /United States/ }));
    expect(await screen.findByText('Enter a valid mobile number for the selected country.')).toBeInTheDocument();
  });

  it('pressing Continue with invalid fields focuses the first one', async () => {
    renderWithBooking(<DetailsStep />, {
      ...base, draftId: 'draft-9', details: { firstName: 'Jane', lastName: '', email: 'jane@gmail', mobile: '+61412345678' },
    });
    const user = userEvent.setup();
    const button = screen.getByRole('button', { name: 'Continue' });
    expect(button).toBeEnabled();
    await user.click(button);
    expect(await screen.findByText('Last name is required.')).toBeInTheDocument();
    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument();
    expect(screen.getByLabelText('Last name')).toHaveFocus();
    expect(apiState.calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  it('updates an existing draft instead of creating one', async () => {
    renderWithBooking(<DetailsStep />, {
      ...base, draftId: 'draft-9', details: { firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678' },
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(currentState().step).toBe('addons'));
    expect(apiState.calls.find((c) => c.method === 'PUT')?.path).toBe('/api/booking-drafts/draft-9');
  });
});
