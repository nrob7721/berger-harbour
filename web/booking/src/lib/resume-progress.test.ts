import { describe, expect, it } from 'vitest';
import { apiState, boats } from '@/testing/server';
import { initialState } from './booking-state';
import { boatNoLongerAvailable, DATES_NO_LONGER_BOOKABLE, resumeProgress } from './resume-progress';
import { toSavedProgress, type SavedProgress } from './saved-progress';

const jane = { firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678' };

function saved(overrides: Partial<SavedProgress> = {}): SavedProgress {
  return {
    ...toSavedProgress(initialState('pacific-blue', {
      period: { start: '2026-10-12', end: '2026-10-16' }, adults: 2, selectedSlug: 'pacific-blue', showSelectedOnly: true, draftId: 'draft-1',
      furthestStep: 'summary',
    })),
    ...overrides,
  };
}

describe('resuming saved progress', () => {
  it('opens the furthest step reached, at most the Summary, with the details read back from the draft', async () => {
    apiState.drafts['draft-1'] = jane;
    const resumed = await resumeProgress(saved({ furthestStep: 'payment' }), boats);
    expect(resumed).toMatchObject({ step: 'summary', details: jane, selectedSlug: 'pacific-blue', period: { start: '2026-10-12' } });
  });

  it('releases a saved hold first, and goes to Complete when the payment already completed', async () => {
    apiState.drafts['draft-1'] = jane;
    const hold = { bookingId: 'b-1', sessionId: 'cs_1' };
    await resumeProgress(saved({ hold, furthestStep: 'payment' }), boats);
    expect(apiState.calls.find((c) => c.path === '/api/bookings/b-1/release')?.body).toEqual({ draftId: 'draft-1' });

    apiState.releaseStatus = 409;
    const completed = await resumeProgress(saved({ hold, furthestStep: 'payment' }), boats);
    expect(completed).toMatchObject({ step: 'complete', completed: { bookingId: 'b-1', sessionId: 'cs_1', email: '' } });
  });

  it('clears dates that no longer pass the online rules, keeping the boat', async () => {
    apiState.drafts['draft-1'] = jane;
    const { server } = await import('@/testing/server');
    const { http, HttpResponse } = await import('msw');
    server.use(http.get('*/api/availability/period', () => HttpResponse.json({ title: 'Too soon', status: 400 }, { status: 400 })));
    const resumed = await resumeProgress(saved(), boats);
    expect(resumed).toMatchObject({ step: 'dates', period: null, selectedSlug: 'pacific-blue', notice: DATES_NO_LONGER_BOOKABLE });
  });

  it('deselects a boat that is no longer free, keeping the dates, on whichever step it resumes', async () => {
    apiState.unavailable['pacific-blue'] = [{ start: '2026-10-13', end: '2026-10-14' }];
    const resumed = await resumeProgress(saved({ draftId: null, furthestStep: 'dates' }), boats);
    expect(resumed).toMatchObject({
      step: 'dates', period: { start: '2026-10-12' }, selectedSlug: null, showSelectedOnly: false, notice: boatNoLongerAvailable('Pacific Blue'),
    });
  });

  it('is discarded when the draft is gone', async () => {
    expect(await resumeProgress(saved(), boats)).toBeNull();
  });

  it('without a draft it goes no further than Your details', async () => {
    const resumed = await resumeProgress(saved({ draftId: null, furthestStep: 'addons' }), boats);
    expect(resumed).toMatchObject({ step: 'details', details: null });
  });
});
