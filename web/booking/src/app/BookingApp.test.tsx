import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { initialState } from '@/lib/booking-state';
import { readSavedProgress, SAVED_PROGRESS_KEY, toSavedProgress } from '@/lib/saved-progress';
import { filledState, renderWithBooking, renderWithClient } from '@/testing/render';
import { apiState } from '@/testing/server';
import { BookingApp } from './BookingApp';

function seed(requestedSlug: string | null, overrides = {}) {
  const progress = toSavedProgress(initialState(requestedSlug, {
    period: { start: '2026-10-12', end: '2026-10-16' }, adults: 2, selectedSlug: 'pacific-blue', draftId: 'draft-1', furthestStep: 'details',
    ...overrides,
  }));
  window.localStorage.setItem(SAVED_PROGRESS_KEY, JSON.stringify(progress));
}

describe('Booking app: saved progress', () => {
  it('a reload resumes the booking with Your details filled from the draft', async () => {
    apiState.drafts['draft-1'] = { firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678' };
    seed('pacific-blue');
    renderWithClient(<BookingApp requestedSlug="pacific-blue" returnedBooking={null} />);
    expect(await screen.findByRole('heading', { name: 'Your details' })).toBeInTheDocument();
    expect(screen.getByLabelText('First name')).toHaveValue('Jane');
    expect(screen.getByLabelText('Email')).toHaveValue('jane@example.com');
  });

  it('is discarded for a different ?boat=', async () => {
    seed('misty-blue');
    renderWithClient(<BookingApp requestedSlug="pacific-blue" returnedBooking={null} />);
    expect(await screen.findByRole('heading', { name: 'Choose your dates and boat' })).toBeInTheDocument();
    await waitFor(() => expect(readSavedProgress()?.requestedSlug).toBe('pacific-blue'));
    expect(apiState.calls.some((c) => c.path.startsWith('/api/booking-drafts/'))).toBe(false);
  });

  it('is discarded when the draft returns 404, starting fresh', async () => {
    seed('pacific-blue');
    renderWithClient(<BookingApp requestedSlug="pacific-blue" returnedBooking={null} />);
    expect(await screen.findByRole('heading', { name: 'Choose your dates and boat' })).toBeInTheDocument();
    await waitFor(() => expect(readSavedProgress()?.draftId).toBeNull());
  });

  it('is deleted when the booking reaches Complete', async () => {
    seed('pacific-blue');
    renderWithBooking(<div />, { ...filledState, step: 'complete', completed: { bookingId: 'b-1', sessionId: 'cs_1', email: 'jane@example.com' } });
    await waitFor(() => expect(window.localStorage.getItem(SAVED_PROGRESS_KEY)).toBeNull());
  });
});
