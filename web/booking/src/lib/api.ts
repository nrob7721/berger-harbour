import { http, query } from '@shared/lib/http';
import type { OnlinePeriodType } from '@shared/utils/periods';
import type {
  BoatDetails, BoatsResponse, BookingStatus, CheckoutResponse, CreateBookingRequest, CreateBookingResponse, CreateDraftRequest, DraftDetails,
  FleetAvailability, PaymentLinkDetails, PeriodAvailability, Quote, QuoteRequest, UpdateDraftRequest,
} from './types';

export const api = {
  boats: () => http.get<BoatsResponse>('/api/boats'),
  boat: (slug: string) => http.get<BoatDetails>(`/api/boats/${encodeURIComponent(slug)}`),
  availability: (from: string, to: string) => http.get<FleetAvailability>(`/api/availability${query({ from, to })}`),
  periodAvailability: (periodType: OnlinePeriodType, startDate: string) =>
    http.get<PeriodAvailability>(`/api/availability/period${query({ periodType, startDate })}`),
  addonRemaining: (addonId: string, startDate: string, endDate: string) =>
    http.get<{ remaining: number }>(`/api/addons/${encodeURIComponent(addonId)}/availability${query({ startDate, endDate })}`),
  createDraft: (body: CreateDraftRequest) => http.post<{ draftId: string }>('/api/booking-drafts', body),
  /** Only the personal details, for resuming saved progress. */
  draft: (draftId: string) => http.get<DraftDetails>(`/api/booking-drafts/${encodeURIComponent(draftId)}`),
  updateDraft: (draftId: string, body: UpdateDraftRequest) => http.put<void>(`/api/booking-drafts/${encodeURIComponent(draftId)}`, body),
  quote: (slug: string, body: QuoteRequest) => http.post<Quote>(`/api/boats/${encodeURIComponent(slug)}/quote`, body),
  createBooking: (body: CreateBookingRequest) => http.post<CreateBookingResponse>('/api/bookings', body),
  release: (bookingId: string, draftId: string) => http.post<void>(`/api/bookings/${encodeURIComponent(bookingId)}/release`, { draftId }),
  status: (bookingId: string, sessionId: string) =>
    http.get<BookingStatus>(`/api/bookings/${encodeURIComponent(bookingId)}/status${query({ session_id: sessionId })}`),
  paymentLink: (token: string) => http.get<PaymentLinkDetails>(`/api/payments/${encodeURIComponent(token)}`),
  paymentCheckout: (token: string) => http.post<CheckoutResponse>(`/api/payments/${encodeURIComponent(token)}/checkout`),
};

/** Query keys. Availability and period results are cached for the session (never stale within it). */
export const keys = {
  boats: ['boats'] as const,
  boat: (slug: string) => ['boat', slug] as const,
  availabilityRoot: ['availability'] as const,
  availability: (from: string, to: string) => ['availability', from, to] as const,
  periodRoot: ['period'] as const,
  period: (type: OnlinePeriodType, start: string) => ['period', type, start] as const,
  addonRemaining: (id: string, start: string, end: string) => ['addon-remaining', id, start, end] as const,
};
