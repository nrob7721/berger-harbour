import { http, HttpResponse, delay } from 'msw';
import { setupServer } from 'msw/node';
import type { Addon, Boat, BookingDetail, Customer, Timeline } from '@/lib/types';

export const boats: Boat[] = [
  {
    id: 'b-pb', name: 'Pacific Blue', slug: 'pacific-blue', type: 'House', berth: 12, adultBeds: 6, kidsBeds: 2, beddingDescription: '4 queens',
    securityBond: 2000, depositAmount: null, imageUrl: null, hasSpa: true, isPetFriendly: false, isActive: true, rates: [], allowedAddonIds: [],
    missingForActivation: [], version: 1,
  },
  {
    id: 'b-mb', name: 'Misty Blue', slug: 'misty-blue', type: 'House', berth: 9, adultBeds: 3, kidsBeds: 3, beddingDescription: 'doubles',
    securityBond: 1500, depositAmount: null, imageUrl: null, hasSpa: false, isPetFriendly: false, isActive: true, rates: [], allowedAddonIds: [],
    missingForActivation: [], version: 1,
  },
];

export const addons: Addon[] = [
  { id: 'ice', name: 'Ice', description: null, priceOnRequest: false, midweekPrice: 5, weekendPrice: 6, weekPrice: 7, quantityApplies: true, capacity: null, capacityUnitLabel: null, isActive: true, version: 1 },
  { id: 'hamper', name: 'Welcome hamper', description: null, priceOnRequest: true, midweekPrice: null, weekendPrice: null, weekPrice: null, quantityApplies: false, capacity: null, capacityUnitLabel: null, isActive: true, version: 1 },
  { id: 'car', name: 'Car parking', description: null, priceOnRequest: true, midweekPrice: null, weekendPrice: null, weekPrice: null, quantityApplies: true, capacity: 11, capacityUnitLabel: 'car spots', isActive: true, version: 1 },
];

export const customer: Customer = {
  id: 'c-1', firstName: 'Jane', lastName: 'Smith', email: 'jane@example.com', mobile: '+61412345678', secondaryMobile: null,
  comments: 'Likes the top deck', createdDate: '2026-01-01T00:00:00Z', version: 3,
};

export function detail(overrides: Partial<BookingDetail> = {}): BookingDetail {
  return {
    id: 'bk-1', reference: 'SMITH-2610120', createdBy: 'SystemUser', isStandby: false, status: 'Active', activatedAt: '2026-08-01T00:00:00Z',
    boatId: 'b-pb', boatName: 'Pacific Blue', periodType: 'Midweek', startDate: '2026-10-12', endDate: '2026-10-16', numberOfAdults: 2,
    numberOfChildren: 0, checkInTime: '11:00', checkOutTime: '08:00', bookingComments: null, hirePrice: 4000, depositAmount: 1000,
    paymentScheduleType: 'Standard', addonLines: [{ id: 'l-1', addonId: 'ice', name: 'Ice', quantity: 2, unitPrice: 5, lineTotal: 10 }],
    payments: [{ id: 'p-1', amount: 1000, method: 'Manual', paidAt: '2026-08-02T02:00:00Z', note: 'Transfer', recordedBy: 'SystemUser' }],
    totalPrice: 4010, amountPaid: 1000, amountOwing: 3010, amountDueNow: 1000, paymentStatus: 'DepositPaid',
    schedule: [
      { kind: 'Deposit', label: 'Deposit', requiredCumulative: 1000, dueDate: '2026-08-01', shortfall: 0 },
      { kind: 'Balance', label: 'Balance (100%)', requiredCumulative: 4010, dueDate: '2026-09-12', shortfall: 3010 },
    ],
    customer, customerDetails: null, frequentFloater: { isFrequentFloater: false, otherBookingCount: 0 }, termsAcceptedAt: null,
    roomingWarningAcceptance: null, holdExpiresAt: null, createdDate: '2026-08-01T00:00:00Z', modifiedDate: '2026-08-01T00:00:00Z', version: 4,
    ...overrides,
  };
}

export const emptyTimeline: Timeline = { boats: boats.map((b) => ({ id: b.id, name: b.name, slug: b.slug, berth: b.berth, isActive: b.isActive })), bookings: [], unavailabilities: [], blockedPeriods: [] };

/** Per-test API state. */
export const api = {
  calls: [] as { method: string; path: string; body?: unknown }[],
  booking: detail() as BookingDetail,
  timeline: emptyTimeline as Timeline,
  lookups: {} as Record<string, { customer: Customer; frequentFloater: { isFrequentFloater: boolean; otherBookingCount: number } }>,
  saveDelay: 20,
  reset() {
    this.calls = [];
    this.booking = detail();
    this.timeline = emptyTimeline;
    this.lookups = {};
    this.saveDelay = 20;
  },
};

const record = async (request: Request) => {
  const url = new URL(request.url);
  const body = request.method === 'GET' ? undefined : await request.clone().json().catch(() => undefined);
  api.calls.push({ method: request.method, path: url.pathname + url.search, body });
};

export const server = setupServer(
  http.get('*/api/boats', async ({ request }) => { await record(request); return HttpResponse.json(boats); }),
  http.get('*/api/settings/addons', async ({ request }) => { await record(request); return HttpResponse.json(addons); }),
  http.get('*/api/settings/business', async ({ request }) => {
    await record(request);
    return HttpResponse.json({ depositAmount: 1000, minimumLeadTimeDays: 30, bookingsOpenUntil: '2027-11-30', staffAlertEmail: null, contactPhone: null, contactEmail: null, hireTermsUrl: 'https://x', accountName: null, bsb: null, accountNumber: null, version: 1 });
  }),
  http.get('*/api/settings/seasons', async ({ request }) => {
    await record(request);
    return HttpResponse.json([{ id: 'normal', name: 'Normal', isDefault: true, ranges: [], version: 1 }]);
  }),
  http.get('*/api/bookings', async ({ request }) => { await record(request); return HttpResponse.json(api.timeline); }),
  http.get('*/api/bookings/:id', async ({ request }) => { await record(request); return HttpResponse.json(api.booking); }),
  http.post('*/api/bookings/quote', async ({ request }) => {
    await record(request);
    return HttpResponse.json({ hirePrice: 4060, addonPrices: [] });
  }),
  http.get('*/api/customers/by-email', async ({ request }) => {
    await record(request);
    const email = new URL(request.url).searchParams.get('email') ?? '';
    const found = api.lookups[email];
    return found ? HttpResponse.json(found) : HttpResponse.json({ title: 'No customer with that email.', status: 404 }, { status: 404 });
  }),
  http.get('*/api/addons/:id/availability', async ({ request }) => { await record(request); return HttpResponse.json({ remaining: 5 }); }),
  http.post('*/api/bookings', async ({ request }) => {
    await record(request);
    await delay(api.saveDelay);
    return HttpResponse.json({ booking: detail({ id: 'bk-new' }), warnings: [] });
  }),
  http.put('*/api/bookings/:id', async ({ request }) => {
    await record(request);
    await delay(api.saveDelay);
    return HttpResponse.json({ booking: api.booking, warnings: [] });
  }),
);
