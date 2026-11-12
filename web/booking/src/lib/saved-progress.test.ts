import { afterEach, describe, expect, it, vi } from 'vitest';
import { initialState } from './booking-state';
import { clearSavedProgress, readSavedProgress, SAVED_PROGRESS_KEY, toSavedProgress, writeSavedProgress } from './saved-progress';

const state = initialState('pacific-blue', {
  step: 'payment', furthestStep: 'payment', period: { start: '2026-10-12', end: '2026-10-16' }, adults: 2, selectedSlug: 'pacific-blue',
  details: { firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678' }, draftId: 'draft-1', addons: { ice: 2 },
  rooming: { slug: 'pacific-blue', adults: 8, children: 2, text: 'Some guests will share.' },
  hold: { bookingId: 'b-1', clientSecret: 'cs_1_secret', sessionId: 'cs_1', checkoutExpiresAt: '2026-10-01T00:30:00Z' },
});

describe('saved progress', () => {
  afterEach(() => vi.restoreAllMocks());

  it('never contains the personal details, the terms tick or the Checkout secret', () => {
    writeSavedProgress(state);
    const raw = window.localStorage.getItem(SAVED_PROGRESS_KEY)!;
    for (const personal of ['Jane', 'Smith', 'jane@example.com', '412345678', 'cs_1_secret', 'terms']) expect(raw).not.toContain(personal);
    expect(JSON.parse(raw)).toMatchObject({ draftId: 'draft-1', furthestStep: 'payment', hold: { bookingId: 'b-1', sessionId: 'cs_1' }, addons: { ice: 2 } });
  });

  it('keeps the hold only on the Payment step', () => {
    expect(toSavedProgress({ ...state, step: 'summary' }).hold).toBeNull();
  });

  it('reads back what was written', () => {
    writeSavedProgress(state);
    expect(readSavedProgress()).toEqual(toSavedProgress(state));
    clearSavedProgress();
    expect(readSavedProgress()).toBeNull();
  });

  it('discards unreadable progress without an error', () => {
    window.localStorage.setItem(SAVED_PROGRESS_KEY, '{not json');
    expect(readSavedProgress()).toBeNull();
    expect(window.localStorage.getItem(SAVED_PROGRESS_KEY)).toBeNull();
    window.localStorage.setItem(SAVED_PROGRESS_KEY, JSON.stringify({ ...toSavedProgress(state), adults: -1 }));
    expect(readSavedProgress()).toBeNull();
  });

  it('works without browser storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('blocked'); });
    expect(() => writeSavedProgress(state)).not.toThrow();
    expect(readSavedProgress()).toBeNull();
  });
});
