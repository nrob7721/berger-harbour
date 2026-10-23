import type { components } from '@shared/api/public';
import { api, query } from '@shared/http';
import type { IsoDate } from '@shared/dates';

type S = components['schemas'];
export type PublicFleet = S['PublicFleetDto'];
export type PublicBookingSettings = S['PublicBookingSettingsDto'];
export type PublicBoat = S['PublicBoatDto'];
export type FleetAvailability = S['FleetAvailabilityDto'];
export type PeriodAvailability = S['PeriodAvailabilityDto'];
export type Quote = S['QuoteDto'];
export type QuoteRequest = S['QuoteRequest'];
export type CreateBookingRequest = S['CreateOnlineBookingRequest'];
export type CreateBookingResult = S['CreateOnlineBookingResult'];
export type BookingStatus = S['BookingStatusDto'];
export type PayPage = S['PayPageDto'];
export type PayCheckout = S['PayCheckoutResult'];
export type CreateDraftRequest = S['CreateBookingDraftRequest'];
export type UpdateDraftRequest = S['UpdateBookingDraftRequest'];
export type BookingStep = S['BookingStep'];

const enc = encodeURIComponent;

export const publicApi = {
  fleet: () => api<PublicFleet>('/api/boats'),
  boat: (slug: string) => api<PublicBoat>(`/api/boats/${enc(slug)}`),
  availability: (from: IsoDate, to: IsoDate) => api<FleetAvailability>(`/api/availability${query({ from, to })}`),
  periodAvailability: (periodType: string, startDate: IsoDate) =>
    api<PeriodAvailability>(`/api/availability/period${query({ periodType, startDate })}`),
  quote: (slug: string, body: QuoteRequest) => api<Quote>(`/api/boats/${enc(slug)}/quote`, { body }),
  addonAvailability: (addonId: string, startDate: IsoDate, endDate: IsoDate) =>
    api<{ remaining: number }>(`/api/addons/${enc(addonId)}/availability${query({ startDate, endDate })}`),
  createDraft: (body: CreateDraftRequest) => api<{ draftId: string }>('/api/booking-drafts', { body }),
  updateDraft: (draftId: string, body: UpdateDraftRequest) =>
    api<void>(`/api/booking-drafts/${enc(draftId)}`, { method: 'PUT', body }),
  createBooking: (body: CreateBookingRequest) => api<CreateBookingResult>('/api/bookings', { body }),
  status: (reference: string, sessionId: string) =>
    api<BookingStatus>(`/api/bookings/${enc(reference)}/status${query({ session_id: sessionId })}`),
  payPage: (token: string) => api<PayPage>(`/api/payments/${enc(token)}`),
  payCheckout: (token: string) => api<PayCheckout>(`/api/payments/${enc(token)}/checkout`, { method: 'POST' }),
};
