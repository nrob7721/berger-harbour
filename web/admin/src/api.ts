import type { components } from '@shared/api/admin';
import { api, query } from '@shared/http';

type S = components['schemas'];
export type Timeline = S['TimelineDto'];
export type TimelineBooking = S['TimelineBookingDto'];
export type BookingDetail = S['BookingDetailDto'];
export type BookingSearchResult = S['BookingSearchResultDto'];
export type AdminBookingRequest = S['AdminBookingRequest'];
export type AdminBookingUpdateRequest = S['AdminBookingUpdateRequest'];
export type AdminQuote = S['AdminQuoteDto'];
export type BookingStatus = S['BookingStatus'];
export type PeriodType = S['PeriodType'];
export type AddonLineStatus = S['AddonLineStatus'];
export type BoatSummary = S['BoatSummaryDto'];
export type BoatDetail = S['BoatDetailDto'];
export type BoatUpsert = S['BoatUpsertRequest'];
export type BoatRate = S['BoatRateDto'];
export type Unavailability = S['UnavailabilityDto'];
export type UnavailabilityRequest = S['UnavailabilityRequest'];
export type Customer = S['CustomerDto'];
export type BusinessSettings = S['BusinessSettingsDto'];
export type Season = S['SeasonDto'];
export type SeasonRequest = S['SeasonRequest'];
export type BlockedPeriod = S['BlockedPeriodDto'];
export type BlockedPeriodRequest = S['BlockedPeriodRequest'];
export type Addon = S['AddonDto'];
export type AddonRequest = S['AddonRequest'];
export type EmailTemplates = S['EmailTemplatesDto'];
export type EmailTemplate = S['EmailTemplateDto'];
export type EmailTemplateKey = S['EmailTemplateKey'];

const enc = encodeURIComponent;
const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body });
const del = (path: string) => api<void>(path, { method: 'DELETE' });

export const adminApi = {
  timeline: (from: string, to: string, includeInactive: boolean) =>
    api<Timeline>(`/api/bookings${query({ from, to, includeInactive })}`),
  search: (q: string) => api<BookingSearchResult[]>(`/api/bookings/search${query({ q })}`),
  booking: (id: string) => api<BookingDetail>(`/api/bookings/${enc(id)}`),
  createBooking: (body: AdminBookingRequest) => api<BookingDetail>('/api/bookings', { body }),
  updateBooking: (id: string, body: AdminBookingUpdateRequest) => put<BookingDetail>(`/api/bookings/${enc(id)}`, body),
  recalculatePrice: (id: string, version: number) =>
    api<BookingDetail>(`/api/bookings/${enc(id)}/recalculate-price`, { body: { version } }),
  addAddonLine: (id: string, version: number, addonId: string, quantity: number) =>
    api<BookingDetail>(`/api/bookings/${enc(id)}/addons`, { body: { version, addonId, quantity } }),
  updateAddonLine: (id: string, lineId: string, body: { version: number; quantity: number; unitPrice: number | null; status: AddonLineStatus }) =>
    put<BookingDetail>(`/api/bookings/${enc(id)}/addons/${enc(lineId)}`, body),
  removeAddonLine: (id: string, lineId: string, version: number) =>
    api<BookingDetail>(`/api/bookings/${enc(id)}/addons/${enc(lineId)}${query({ version })}`, { method: 'DELETE' }),
  addPayment: (id: string, body: { version: number; amount: number; date: string; note: string | null }) =>
    api<BookingDetail>(`/api/bookings/${enc(id)}/payments`, { body }),
  sendPaymentLink: (id: string) =>
    api<{ sentTo: string; amountDue: number }>(`/api/bookings/${enc(id)}/send-payment-link`, { method: 'POST' }),
  quote: (boatId: string, periodType: PeriodType, startDate: string) =>
    api<AdminQuote>(`/api/bookings/quote${query({ boatId, periodType, startDate })}`),

  boats: () => api<BoatSummary[]>('/api/boats'),
  boat: (id: string) => api<BoatDetail>(`/api/boats/${enc(id)}`),
  createBoat: (body: BoatUpsert) => api<BoatDetail>('/api/boats', { body }),
  updateBoat: (id: string, body: BoatUpsert) => put<BoatDetail>(`/api/boats/${enc(id)}`, body),
  unavailabilities: (boatId: string) => api<Unavailability[]>(`/api/boats/${enc(boatId)}/unavailabilities`),
  createUnavailability: (boatId: string, body: UnavailabilityRequest) =>
    api<Unavailability>(`/api/boats/${enc(boatId)}/unavailabilities`, { body }),
  updateUnavailability: (boatId: string, id: string, body: UnavailabilityRequest) =>
    put<Unavailability>(`/api/boats/${enc(boatId)}/unavailabilities/${enc(id)}`, body),
  deleteUnavailability: (boatId: string, id: string, version: number) =>
    del(`/api/boats/${enc(boatId)}/unavailabilities/${enc(id)}${query({ version })}`),

  customers: (q?: string) => api<Customer[]>(`/api/customers${query({ q })}`),
  customerByEmail: (email: string) => api<Customer>(`/api/customers/by-email${query({ email })}`),

  business: () => api<BusinessSettings>('/api/settings/business'),
  updateBusiness: (body: BusinessSettings) => put<BusinessSettings>('/api/settings/business', body),
  seasons: () => api<Season[]>('/api/settings/seasons'),
  createSeason: (body: SeasonRequest) => api<Season>('/api/settings/seasons', { body }),
  updateSeason: (id: string, body: SeasonRequest) => put<Season>(`/api/settings/seasons/${enc(id)}`, body),
  deleteSeason: (id: string, version: number) => del(`/api/settings/seasons/${enc(id)}${query({ version })}`),
  blockedPeriods: () => api<BlockedPeriod[]>('/api/settings/blocked-periods'),
  createBlockedPeriod: (body: BlockedPeriodRequest) => api<BlockedPeriod>('/api/settings/blocked-periods', { body }),
  updateBlockedPeriod: (id: string, body: BlockedPeriodRequest) => put<BlockedPeriod>(`/api/settings/blocked-periods/${enc(id)}`, body),
  deleteBlockedPeriod: (id: string, version: number) => del(`/api/settings/blocked-periods/${enc(id)}${query({ version })}`),
  addons: () => api<Addon[]>('/api/settings/addons'),
  createAddon: (body: AddonRequest) => api<Addon>('/api/settings/addons', { body }),
  updateAddon: (id: string, body: AddonRequest) => put<Addon>(`/api/settings/addons/${enc(id)}`, body),
  deactivateAddon: (id: string, version: number) => del(`/api/settings/addons/${enc(id)}${query({ version })}`),
  templates: () => api<EmailTemplates>('/api/settings/email-templates'),
  updateTemplate: (key: EmailTemplateKey, body: { version: number; subject: string; htmlBody: string }) =>
    put<EmailTemplate>(`/api/settings/email-templates/${enc(key)}`, body),
  testTemplate: (key: EmailTemplateKey) => api<void>(`/api/settings/email-templates/${enc(key)}/test`, { method: 'POST' }),
};
