import type { components } from '@shared/api/public';
import { api, query } from '@shared/http';
import type { IsoDate } from '@shared/dates';

type S = components['schemas'];
export type PublicBoat = S['PublicBoatDto'];
export type Availability = S['AvailabilityDto'];
export type Quote = S['QuoteDto'];
export type QuoteRequest = S['QuoteRequest'];
export type CreateBookingRequest = S['CreateOnlineBookingRequest'];
export type CreateBookingResult = S['CreateOnlineBookingResult'];
export type BookingStatus = S['BookingStatusDto'];
export type PayPage = S['PayPageDto'];
export type PayCheckout = S['PayCheckoutResult'];

const enc = encodeURIComponent;

export const publicApi = {
  boat: (slug: string) => api<PublicBoat>(`/api/boats/${enc(slug)}`),
  availability: (slug: string, from: IsoDate, to: IsoDate) =>
    api<Availability>(`/api/boats/${enc(slug)}/availability${query({ from, to })}`),
  quote: (slug: string, body: QuoteRequest) => api<Quote>(`/api/boats/${enc(slug)}/quote`, { body }),
  createBooking: (body: CreateBookingRequest) => api<CreateBookingResult>('/api/bookings', { body }),
  status: (reference: string, sessionId: string) =>
    api<BookingStatus>(`/api/bookings/${enc(reference)}/status${query({ session_id: sessionId })}`),
  payPage: (token: string) => api<PayPage>(`/api/payments/${enc(token)}`),
  payCheckout: (token: string) => api<PayCheckout>(`/api/payments/${enc(token)}/checkout`, { method: 'POST' }),
};
