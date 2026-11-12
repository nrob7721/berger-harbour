import { http, HttpResponse, delay } from 'msw';
import { setupServer } from 'msw/node';
import { staysOverlap } from '@shared/utils/periods';
import { addDays } from '@shared/utils/dates';
import type { BoatDetails, BoatSummary, FleetAvailability, PublicAddon } from '@/lib/types';

// A small fleet in berth order. "Today" is Sat 01/08/2026 (AEST), so October 2026 onwards is bookable.
export const settings = {
  minimumLeadTimeDays: 30,
  bookingsOpenUntil: '2027-11-30',
  today: '2026-08-01',
  contactPhone: '02 4455 6677',
  contactEmail: 'info@berger.test',
  hireTermsUrl: 'https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/',
};

export const boats: BoatSummary[] = [
  { slug: 'kalinda', name: 'Kalinda', imageUrl: null, berth: 12, hasSpa: false, isPetFriendly: false },
  { slug: 'pacific-blue', name: 'Pacific Blue', imageUrl: null, berth: 12, hasSpa: true, isPetFriendly: false },
  { slug: 'misty-blue', name: 'Misty Blue', imageUrl: null, berth: 9, hasSpa: false, isPetFriendly: true },
  { slug: 'runaway-bear', name: 'Runaway Bear', imageUrl: null, berth: 2, hasSpa: false, isPetFriendly: false },
];

export const prices: Record<string, number> = { kalinda: 2535, 'pacific-blue': 4060, 'misty-blue': 1500, 'runaway-bear': 880 };

export const addons: PublicAddon[] = [
  { id: 'ice', name: 'Ice', description: null, priceOnRequest: false, quantityApplies: true, prices: { midweek: 5, weekend: 5, week: 5 }, hasCapacity: false, capacityUnitLabel: null },
  { id: 'car-parking', name: 'Car parking', description: null, priceOnRequest: true, quantityApplies: true, prices: { midweek: null, weekend: null, week: null }, hasCapacity: true, capacityUnitLabel: 'car spots' },
  { id: 'outboard-motor', name: 'Outboard motor', description: null, priceOnRequest: false, quantityApplies: false, prices: { midweek: 95, weekend: 95, week: 130 }, hasCapacity: false, capacityUnitLabel: null },
  { id: 'welcome-hamper', name: 'Welcome hamper', description: null, priceOnRequest: true, quantityApplies: false, prices: { midweek: null, weekend: null, week: null }, hasCapacity: false, capacityUnitLabel: null },
];

export function details(slug: string, overrides: Partial<BoatDetails> = {}): BoatDetails {
  const b = boats.find((x) => x.slug === slug) ?? { slug, name: slug, imageUrl: null, berth: 10, hasSpa: false, isPetFriendly: false };
  return {
    ...b, adultBeds: Math.min(6, b.berth), kidsBeds: b.berth >= 8 ? 2 : 0, beddingDescription: '4 queens and bunks', securityBond: 2000,
    depositAmount: 1000, isActive: true, addons, ...overrides,
  };
}

/** Mutable per-test API state. */
export const apiState = {
  /** Unavailable stays per slug. */
  unavailable: {} as Record<string, { start: string; end: string }[]>,
  enquireOnly: [] as { name: string; firstNight: string; lastNight: string }[],
  boatDetails: {} as Record<string, BoatDetails | null>,
  remaining: 6,
  /** Drafts readable with GET /api/booking-drafts/{id}; any other id is 404. */
  drafts: {} as Record<string, { firstName: string; lastName: string; email: string; mobile: string }>,
  /** The status POST …/release answers with. */
  releaseStatus: 204,
  calls: [] as { method: string; path: string; body?: unknown }[],
};

export function resetApi() {
  apiState.unavailable = {};
  apiState.enquireOnly = [];
  apiState.boatDetails = {};
  apiState.remaining = 6;
  apiState.drafts = {};
  apiState.releaseStatus = 204;
  apiState.calls = [];
}

const record = async (request: Request) => {
  const url = new URL(request.url);
  const body = request.method === 'GET' ? undefined : await request.clone().json().catch(() => undefined);
  apiState.calls.push({ method: request.method, path: url.pathname, body });
};

export const handlers = [
  http.get('*/api/boats', async ({ request }) => {
    await record(request);
    return HttpResponse.json({ boats, settings });
  }),
  http.get('*/api/boats/:slug', async ({ request, params }) => {
    await record(request);
    const slug = String(params.slug);
    if (slug in apiState.boatDetails) {
      const d = apiState.boatDetails[slug];
      return d ? HttpResponse.json(d) : HttpResponse.json({ title: 'Not found', status: 404 }, { status: 404 });
    }
    return HttpResponse.json(details(slug));
  }),
  http.get('*/api/availability', async ({ request }) => {
    await record(request);
    const result: FleetAvailability = {
      enquireOnly: apiState.enquireOnly,
      boats: boats.map((b) => ({ slug: b.slug, unavailable: apiState.unavailable[b.slug] ?? [] })),
    };
    return HttpResponse.json(result);
  }),
  http.get('*/api/availability/period', async ({ request }) => {
    await record(request);
    const url = new URL(request.url);
    const type = url.searchParams.get('periodType')!;
    const start = url.searchParams.get('startDate')!;
    const end = addDays(start, type === 'Midweek' ? 4 : type === 'Weekend' ? 3 : 7);
    return HttpResponse.json({
      periodType: type, startDate: start, endDate: end,
      boats: boats.map((b) => ({
        slug: b.slug,
        available: !(apiState.unavailable[b.slug] ?? []).some((r) => staysOverlap(r, { start, end })),
        hirePrice: prices[b.slug],
      })),
    });
  }),
  http.get('*/api/addons/:id/availability', async ({ request }) => {
    await record(request);
    return HttpResponse.json({ remaining: apiState.remaining });
  }),
  http.post('*/api/booking-drafts', async ({ request }) => {
    await record(request);
    await delay(30);
    return HttpResponse.json({ draftId: 'draft-1' });
  }),
  http.get('*/api/booking-drafts/:id', async ({ request, params }) => {
    await record(request);
    const draft = apiState.drafts[params.id as string];
    return draft
      ? HttpResponse.json(draft)
      : HttpResponse.json({ title: 'Your booking session has expired. Please start again.', status: 404 }, { status: 404 });
  }),
  http.post('*/api/bookings/:id/release', async ({ request }) => {
    await record(request);
    return apiState.releaseStatus === 204
      ? new HttpResponse(null, { status: 204 })
      : HttpResponse.json({ title: 'Your payment has already been completed.', status: apiState.releaseStatus }, { status: apiState.releaseStatus });
  }),
  http.put('*/api/booking-drafts/:id', async ({ request }) => {
    await record(request);
    return new HttpResponse(null, { status: 204 });
  }),
];

export const server = setupServer(...handlers);
