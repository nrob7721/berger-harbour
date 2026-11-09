import { http, query } from '@shared/lib/http';
import type {
  Addon, AdminQuote, AdminQuoteRequest, Boat, BlockedPeriod, BookingDetail, BusinessSettings, CreateBookingRequest, Customer,
  CustomerBooking, CustomerLookup, EmailTemplate, EmailTemplateKey, EmailTemplates, ManualPaymentRequest, SaveAddonRequest,
  SaveBlockedPeriodRequest, SaveBoatRequest, SaveResponse, SaveSeasonRequest, SaveUnavailabilityRequest, SearchResult, Season, Timeline,
  Unavailability, UpdateBookingRequest,
} from './types';

const id = encodeURIComponent;

export const api = {
  timeline: (from: string, to: string, includeInactive: boolean) => http.get<Timeline>(`/api/bookings${query({ from, to, includeInactive })}`),
  search: (q: string) => http.get<SearchResult[]>(`/api/bookings/search${query({ q })}`),
  booking: (bookingId: string) => http.get<BookingDetail>(`/api/bookings/${id(bookingId)}`),
  createBooking: (body: CreateBookingRequest) => http.post<SaveResponse>('/api/bookings', body),
  updateBooking: (bookingId: string, body: UpdateBookingRequest) => http.put<SaveResponse>(`/api/bookings/${id(bookingId)}`, body),
  quote: (body: AdminQuoteRequest) => http.post<AdminQuote>('/api/bookings/quote', body),
  addPayment: (bookingId: string, body: ManualPaymentRequest) => http.post<BookingDetail>(`/api/bookings/${id(bookingId)}/payments`, body),
  sendPaymentLink: (bookingId: string) => http.post<void>(`/api/bookings/${id(bookingId)}/send-payment-link`),
  addonRemaining: (addonId: string, startDate: string, endDate: string, excludeBookingId?: string) =>
    http.get<{ remaining: number }>(`/api/addons/${id(addonId)}/availability${query({ startDate, endDate, excludeBookingId })}`),

  boats: () => http.get<Boat[]>('/api/boats'),
  createBoat: (body: SaveBoatRequest) => http.post<Boat>('/api/boats', body),
  updateBoat: (boatId: string, body: SaveBoatRequest) => http.put<Boat>(`/api/boats/${id(boatId)}`, body),
  unavailabilities: (boatId: string) => http.get<Unavailability[]>(`/api/boats/${id(boatId)}/unavailabilities`),
  createUnavailability: (boatId: string, body: SaveUnavailabilityRequest) => http.post<Unavailability>(`/api/boats/${id(boatId)}/unavailabilities`, body),
  updateUnavailability: (boatId: string, uid: string, body: SaveUnavailabilityRequest) =>
    http.put<Unavailability>(`/api/boats/${id(boatId)}/unavailabilities/${id(uid)}`, body),
  deleteUnavailability: (boatId: string, uid: string) => http.delete<void>(`/api/boats/${id(boatId)}/unavailabilities/${id(uid)}`),

  customers: () => http.get<Customer[]>('/api/customers'),
  customerByEmail: (email: string) => http.get<CustomerLookup>(`/api/customers/by-email${query({ email })}`),
  updateCustomerComments: (customerId: string, comments: string | null, version: number) =>
    http.put<Customer>(`/api/customers/${id(customerId)}/comments`, { comments, version }),
  customerBookings: (customerId: string) => http.get<CustomerBooking[]>(`/api/customers/${id(customerId)}/bookings`),

  business: () => http.get<BusinessSettings>('/api/settings/business'),
  updateBusiness: (body: BusinessSettings) => http.put<BusinessSettings>('/api/settings/business', body),
  seasons: () => http.get<Season[]>('/api/settings/seasons'),
  createSeason: (body: SaveSeasonRequest) => http.post<Season>('/api/settings/seasons', body),
  updateSeason: (seasonId: string, body: SaveSeasonRequest) => http.put<Season>(`/api/settings/seasons/${id(seasonId)}`, body),
  deleteSeason: (seasonId: string) => http.delete<void>(`/api/settings/seasons/${id(seasonId)}`),
  blockedPeriods: () => http.get<BlockedPeriod[]>('/api/settings/blocked-periods'),
  createBlockedPeriod: (body: SaveBlockedPeriodRequest) => http.post<BlockedPeriod>('/api/settings/blocked-periods', body),
  updateBlockedPeriod: (periodId: string, body: SaveBlockedPeriodRequest) => http.put<BlockedPeriod>(`/api/settings/blocked-periods/${id(periodId)}`, body),
  deleteBlockedPeriod: (periodId: string) => http.delete<void>(`/api/settings/blocked-periods/${id(periodId)}`),
  addons: () => http.get<Addon[]>('/api/settings/addons'),
  createAddon: (body: SaveAddonRequest) => http.post<Addon>('/api/settings/addons', body),
  updateAddon: (addonId: string, body: SaveAddonRequest) => http.put<Addon>(`/api/settings/addons/${id(addonId)}`, body),
  deactivateAddon: (addonId: string) => http.delete<Addon>(`/api/settings/addons/${id(addonId)}`),
  reorderAddons: (addonIds: string[]) => http.put<Addon[]>('/api/settings/addons/order', { addonIds }),
  templates: () => http.get<EmailTemplates>('/api/settings/email-templates'),
  updateTemplate: (key: EmailTemplateKey, subject: string, htmlBody: string, version: number) =>
    http.put<EmailTemplate>(`/api/settings/email-templates/${key}`, { subject, htmlBody, version }),
  sendTestTemplate: (key: EmailTemplateKey) => http.post<void>(`/api/settings/email-templates/${key}/test`),
};

export const keys = {
  timelineRoot: ['timeline'] as const,
  timeline: (from: string, to: string, includeInactive: boolean) => ['timeline', from, to, includeInactive] as const,
  booking: (bookingId: string) => ['booking', bookingId] as const,
  search: (q: string) => ['search', q] as const,
  boats: ['boats'] as const,
  unavailabilities: (boatId: string) => ['unavailabilities', boatId] as const,
  customers: ['customers'] as const,
  customerBookings: (customerId: string) => ['customer-bookings', customerId] as const,
  business: ['settings', 'business'] as const,
  seasons: ['settings', 'seasons'] as const,
  blockedPeriods: ['settings', 'blocked-periods'] as const,
  addons: ['settings', 'addons'] as const,
  templates: ['settings', 'templates'] as const,
  addonRemaining: (addonId: string, start: string, end: string, exclude: string | null) => ['addon-remaining', addonId, start, end, exclude] as const,
};
