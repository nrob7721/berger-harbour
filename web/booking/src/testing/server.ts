import { http, HttpResponse, delay } from 'msw';
import { setupServer } from 'msw/node';
import { staysOverlap } from '@shared/utils/periods';
import { addDays } from '@shared/utils/dates';
import type { BoatDetails, BoatImageInfo, BoatRate, BoatSummary, FleetAvailability, FleetDetails, PublicAddon, PublicSeason } from '@/lib/types';

// A small fleet in berth order. "Today" is Sat 01/08/2026 (AEST), so October 2026 onwards is bookable.
export const settings = {
  minimumLeadTimeDays: 30,
  bookingsOpenUntil: '2027-11-30',
  today: '2026-08-01',
  contactPhone: '02 4455 6677',
  contactEmail: 'info@berger.test',
  hireTermsUrl: 'https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/',
};

const photo = (name: string, description: string | null = null): BoatImageInfo =>
  ({ url: `https://bergerhouseboats.com.au/wp-content/uploads/${name}.jpg`, description });

/** Each boat's photos; Runaway Bear has none. */
export const photos: Record<string, BoatImageInfo[]> = {
  kalinda: [photo('kalinda-1'), photo('kalinda-2', 'Kalinda kitchen')],
  'pacific-blue': [photo('pb-1'), photo('pb-2'), photo('pb-3', 'Upper deck with a heated spa'), photo('pb-4', 'Kitchen')],
  'misty-blue': [photo('mb-1')],
  'runaway-bear': [],
};

export const boats: BoatSummary[] = [
  { slug: 'kalinda', name: 'Kalinda', thumbnailUrl: photos.kalinda[0].url, berth: 12, hasSpa: false, isPetFriendly: false,
    tags: ['Sleeps 12', '3 double bedrooms', 'Bunk room', 'Air conditioning', 'Generator', 'Upper-deck BBQ', 'Esky'] },
  { slug: 'pacific-blue', name: 'Pacific Blue', thumbnailUrl: photos['pacific-blue'][0].url, berth: 12, hasSpa: true, isPetFriendly: false,
    tags: ['Sleeps 12', 'Heated spa'] },
  { slug: 'misty-blue', name: 'Misty Blue', thumbnailUrl: photos['misty-blue'][0].url, berth: 9, hasSpa: false, isPetFriendly: true, tags: [] },
  { slug: 'runaway-bear', name: 'Runaway Bear', thumbnailUrl: '', berth: 2, hasSpa: false, isPetFriendly: false, tags: ['Sleeps 2'] },
];

export const seasons: PublicSeason[] = [
  { id: 'normal', name: 'Normal', ranges: [] },
  { id: 'off-peak', name: 'Off Peak', ranges: [{ start: '01-05', end: '31-08' }] },
  { id: 'peak', name: 'Peak', ranges: [{ start: '01-12', end: '31-01' }] },
];

/** Every season × Midweek, Weekend, Week and Long weekend, except Peak Long weekend (shown as "—"). */
export function rates(base: number): BoatRate[] {
  return seasons.flatMap((s, i) => (['Midweek', 'Weekend', 'Week', 'LongWeekend'] as const)
    .filter((t) => !(s.id === 'peak' && t === 'LongWeekend'))
    .map((periodType, j) => ({ seasonId: s.id, periodType, price: base + i * 100 + j * 10 })));
}

export const prices: Record<string, number> = { kalinda: 2535, 'pacific-blue': 4060, 'misty-blue': 1500, 'runaway-bear': 880 };

export const addons: PublicAddon[] = [
  { id: 'ice', name: 'Ice', description: null, priceOnRequest: false, quantityApplies: true, prices: { midweek: 5, weekend: 5, week: 5 }, hasCapacity: false, capacityUnitLabel: null },
  { id: 'car-parking', name: 'Car parking', description: null, priceOnRequest: true, quantityApplies: true, prices: { midweek: null, weekend: null, week: null }, hasCapacity: true, capacityUnitLabel: 'car spots' },
  { id: 'outboard-motor', name: 'Outboard motor', description: null, priceOnRequest: false, quantityApplies: false, prices: { midweek: 95, weekend: 95, week: 130 }, hasCapacity: false, capacityUnitLabel: null },
  { id: 'welcome-hamper', name: 'Welcome hamper', description: null, priceOnRequest: true, quantityApplies: false, prices: { midweek: null, weekend: null, week: null }, hasCapacity: false, capacityUnitLabel: null },
];

export function details(slug: string, overrides: Partial<BoatDetails> = {}): BoatDetails {
  const b = boats.find((x) => x.slug === slug) ?? { slug, name: slug, thumbnailUrl: '', berth: 10, hasSpa: false, isPetFriendly: false, tags: [] };
  return {
    ...b, images: photos[slug] ?? [], adultBeds: Math.min(6, b.berth), kidsBeds: b.berth >= 8 ? 2 : 0, beddingDescription: '4 queens and bunks',
    securityBond: 2000, depositAmount: 1000, rates: rates(prices[slug] ?? 1000), isActive: true, addons, ...overrides,
  };
}

/** `GET /api/boat-details` for the fleet above. */
export function fleetDetails(): FleetDetails {
  return {
    boats: boats.map((b) => {
      const d = details(b.slug);
      return { slug: b.slug, images: d.images, beddingDescription: d.beddingDescription, securityBond: d.securityBond, depositAmount: d.depositAmount, rates: d.rates };
    }),
    seasons,
  };
}

/** Mutable per-test API state. */
export const apiState = {
  /** Unavailable stays per slug. */
  unavailable: {} as Record<string, { start: string; end: string }[]>,
  enquireOnly: [] as { name: string; firstNight: string; lastNight: string }[],
  boatDetails: {} as Record<string, BoatDetails | null>,
  /** How `GET /api/boat-details` answers: at once, after `fleetDetailsDelay` ms, or with a 500. */
  fleetDetails: 'ok' as 'ok' | 'error',
  fleetDetailsDelay: 0,
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
  apiState.fleetDetails = 'ok';
  apiState.fleetDetailsDelay = 0;
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
  http.get('*/api/boat-details', async ({ request }) => {
    await record(request);
    if (apiState.fleetDetailsDelay) await delay(apiState.fleetDetailsDelay);
    return apiState.fleetDetails === 'ok'
      ? HttpResponse.json(fleetDetails())
      : HttpResponse.json({ title: 'Something went wrong.', status: 500 }, { status: 500 });
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
