import type { ReactNode } from 'react';
import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BookingProvider, initialState, useBooking, type BookingState } from '@/lib/booking-state';

export function testQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } } });
}

let latest: BookingState | null = null;

/** The latest booking state, for assertions. */
export const currentState = () => latest!;

function Probe() {
  latest = useBooking().state;
  return <div data-testid="step">{latest.step}</div>;
}

export function renderWithBooking(ui: ReactNode, state: Partial<BookingState> = {}, client = testQueryClient()) {
  const result = render(
    <QueryClientProvider client={client}>
      <BookingProvider initial={initialState(state.requestedSlug ?? null, state)}>
        {ui}
        <Probe />
      </BookingProvider>
    </QueryClientProvider>,
  );
  return { ...result, client };
}

export function renderWithClient(ui: ReactNode, client = testQueryClient()) {
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

/** A booking that has reached a later step. */
export const filledState: Partial<BookingState> = {
  periodType: 'Midweek',
  period: { start: '2026-10-12', end: '2026-10-16' },
  adults: 2,
  children: 0,
  selectedSlug: 'pacific-blue',
  details: { firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678' },
  draftId: 'draft-1',
};
