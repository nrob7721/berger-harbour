import { describe, expect, it } from 'vitest';
import { evaluateFleetDay, type BookingWindow, type FleetAvailabilityData } from '@shared/periods';
import {
  baseHireRateText, boatList, calendarBoats, candidateBoats, chooseBoat, inDisplayOrder, keepChoiceValid, largestBoatGuests,
  onlySelectedCheckbox, removeDisallowedAddons, requestedBoatReason, roomingAcceptanceHolds,
  type BoatChoice, type Filters, type FleetBoat, type PeriodBoat, type RequestedBoat,
} from './fleet';
import { childOptions, clampChildren } from './rules';

const boat = (slug: string, maxNoOfGuests: number, features: Partial<FleetBoat> = {}): FleetBoat => ({
  slug, name: slug.split('-').map((w) => w[0]!.toUpperCase() + w.slice(1)).join(' '), imageUrl: null, maxNoOfGuests,
  hasSpa: false, isPetFriendly: false, ...features,
});

const pacificBlue = boat('pacific-blue', 12, { hasSpa: true });
const kalinda = boat('kalinda', 12);
const wanderer = boat('wanderer', 6, { isPetFriendly: true });
const runawayBear = boat('runaway-bear', 2);
const fleet = [wanderer, pacificBlue, runawayBear, kalinda];

const requested = (b: FleetBoat, isActive = true): RequestedBoat => ({ ...b, isActive });
const filters = (f: Partial<Filters> = {}): Filters => ({ adults: 1, children: 0, spa: false, petFriendly: false, ...f });
const period = (...results: [string, boolean, number?][]) =>
  new Map<string, PeriodBoat>(results.map(([slug, available, hirePrice]) => [slug, { slug, available, hirePrice: hirePrice ?? 1000 }]));

const slugs = (boats: { boat: FleetBoat }[]) => boats.map((c) => c.boat.slug);

describe('candidates and ordering', () => {
  it('lists boats largest first, then by name', () => {
    expect(inDisplayOrder(fleet).map((b) => b.slug)).toEqual(['kalinda', 'pacific-blue', 'wanderer', 'runaway-bear']);
  });

  it('candidates take the guests and have every ticked feature', () => {
    expect(candidateBoats(fleet, filters({ adults: 3 })).map((b) => b.slug)).toEqual(['wanderer', 'pacific-blue', 'kalinda']);
    expect(candidateBoats(fleet, filters({ spa: true })).map((b) => b.slug)).toEqual(['pacific-blue']);
    expect(candidateBoats(fleet, filters({ petFriendly: true })).map((b) => b.slug)).toEqual(['wanderer']);
    expect(candidateBoats(fleet, filters({ spa: true, petFriendly: true }))).toEqual([]);
  });

  it('guest dropdowns go up to the largest boat, and children are clamped to it', () => {
    const n = largestBoatGuests(fleet);
    expect(n).toBe(12);
    expect(childOptions(n, 10)).toEqual([0, 1, 2]);
    expect(clampChildren(n, 11, 4)).toBe(1);
  });
});

describe('calendar greying', () => {
  // Mid-week Mon 12/10/2026 → Fri 16/10: pacific-blue and wanderer are taken, kalinda is free.
  const window: BookingWindow = { earliestStart: '2026-10-01', bookingsOpenUntil: '2027-11-30' };
  const availability: FleetAvailabilityData = {
    enquireOnly: [],
    unavailable: new Map([
      ['pacific-blue', [{ start: '2026-10-12', end: '2026-10-16' }]],
      ['wanderer', [{ start: '2026-10-12', end: '2026-10-16' }]],
    ]),
  };
  const status = (boats: string[]) => evaluateFleetDay('Midweek', '2026-10-13', availability, boats, window).status;

  it('unticked: a period is free when any candidate boat is free for it', () => {
    expect(status(calendarBoats(fleet, filters(), null, false))).toBe('available');
    expect(status(calendarBoats(fleet, filters(), 'pacific-blue', false))).toBe('available');
  });

  it('unticked: the guest, Spa and Pet friendly filters decide which boats count', () => {
    expect(status(calendarBoats(fleet, filters({ spa: true }), null, false))).toBe('taken');
    expect(status(calendarBoats(fleet, filters({ petFriendly: true }), null, false))).toBe('taken');
    expect(status(calendarBoats(fleet, filters({ adults: 12, children: 1 }), null, false))).toBe('taken'); // no boat takes 13
  });

  it('ticked: only the selected boat counts', () => {
    expect(calendarBoats(fleet, filters(), 'pacific-blue', true)).toEqual(['pacific-blue']);
    expect(status(calendarBoats(fleet, filters(), 'pacific-blue', true))).toBe('taken');
    expect(status(calendarBoats(fleet, filters(), 'kalinda', true))).toBe('available');
  });
});

describe('"Show {Boat}\'s availability only" checkbox', () => {
  it('is unticked and disabled without a selected boat', () => {
    expect(onlySelectedCheckbox(null, true)).toEqual({ label: "Show the selected boat's availability only", checked: false, disabled: true });
  });

  it('is enabled once a boat is selected, unticked until the customer ticks it', () => {
    const choice = chooseBoat({ selected: null, onlySelected: false }, 'kalinda');
    expect(onlySelectedCheckbox('Kalinda', choice.onlySelected)).toEqual({ label: "Show Kalinda's availability only", checked: false, disabled: false });
    expect(onlySelectedCheckbox('Kalinda', true).checked).toBe(true);
  });

  it('selecting another card leaves it unchanged, and the calendar follows the new boat', () => {
    expect(chooseBoat({ selected: 'pacific-blue', onlySelected: true }, 'kalinda')).toEqual({ selected: 'kalinda', onlySelected: true });
    expect(chooseBoat({ selected: 'pacific-blue', onlySelected: false }, 'kalinda')).toEqual({ selected: 'kalinda', onlySelected: false });
    expect(calendarBoats(fleet, filters(), 'kalinda', true)).toEqual(['kalinda']);
  });
});

describe('keeping the selection valid', () => {
  const ticked: BoatChoice = { selected: 'pacific-blue', onlySelected: true };

  it('keeps an eligible boat', () => {
    expect(keepChoiceValid(ticked, fleet, filters({ adults: 10 }), period(['pacific-blue', true]))).toBe(ticked);
    expect(keepChoiceValid(ticked, fleet, filters(), null)).toBe(ticked); // period results not known yet
  });

  it('deselects the boat and unticks the checkbox when the period, guests or a filter rule it out', () => {
    const cleared = { selected: null, onlySelected: false };
    expect(keepChoiceValid(ticked, fleet, filters(), period(['pacific-blue', false]))).toEqual(cleared);
    expect(keepChoiceValid(ticked, fleet, filters({ adults: 12, children: 1 }), null)).toEqual(cleared);
    expect(keepChoiceValid(ticked, fleet, filters({ petFriendly: true }), null)).toEqual(cleared);
    expect(keepChoiceValid({ selected: 'gone', onlySelected: false }, fleet, filters(), null)).toEqual(cleared);
  });
});

describe('boat cards', () => {
  it('the requested boat is always first, and the others follow in display order', () => {
    const list = boatList(fleet, requested(wanderer), filters(), null);
    expect(list.requested?.boat.slug).toBe('wanderer');
    expect(list.separator).toBe(true);
    expect(slugs(list.others)).toEqual(['kalinda', 'pacific-blue', 'runaway-bear']);
    expect(list.emptyMessage).toBeNull();
  });

  it('without a requested boat there is no separator', () => {
    const list = boatList(fleet, null, filters(), null);
    expect(list.requested).toBeNull();
    expect(list.separator).toBe(false);
    expect(slugs(list.others)).toEqual(['kalinda', 'pacific-blue', 'wanderer', 'runaway-bear']);
  });

  it('the requested boat stays first even when it cannot be selected', () => {
    const list = boatList(fleet, requested(runawayBear), filters({ adults: 4 }), null);
    expect(list.requested?.boat.slug).toBe('runaway-bear');
    expect(list.requested?.reason).toBe('Too small for 4 guests');
  });

  it("the requested boat's reason follows inactive → dates → too small → filters", () => {
    const all = filters({ adults: 13, spa: true });
    const taken = period(['wanderer', false]);
    expect(requestedBoatReason(requested(wanderer, false), all, taken)).toBe('Not available for online booking');
    expect(requestedBoatReason(requested(wanderer), all, taken)).toBe('Not available for selected dates');
    expect(requestedBoatReason(requested(wanderer), all, period(['wanderer', true]))).toBe('Too small for 13 guests');
    expect(requestedBoatReason(requested(wanderer), filters({ spa: true }), null)).toBe("Doesn't match your filters");
    expect(requestedBoatReason(requested(wanderer), filters({ petFriendly: true }), null)).toBeNull();
  });

  it('before a period is selected every candidate is shown; after, only those free for it, with their price', () => {
    const results = period(['kalinda', true, 4060], ['pacific-blue', false, 4500], ['wanderer', true, 1210], ['runaway-bear', true, 880]);
    const list = boatList(fleet, requested(pacificBlue), filters({ adults: 3 }), results);
    expect(list.requested?.reason).toBe('Not available for selected dates');
    expect(list.requested?.hirePrice).toBeNull();
    expect(list.others.map((c) => [c.boat.slug, c.hirePrice])).toEqual([['kalinda', 4060], ['wanderer', 1210]]);
  });

  it('says when no other boat is free, or when no candidate at all is free', () => {
    const onlyRequestedFree = boatList(fleet, requested(kalinda), filters(),
      period(['kalinda', true], ['pacific-blue', false], ['wanderer', false], ['runaway-bear', false]));
    expect(onlyRequestedFree.others).toEqual([]);
    expect(onlyRequestedFree.emptyMessage).toBe('No other boats are available for these dates.');

    const noneFree = boatList(fleet, requested(kalinda), filters({ petFriendly: true }),
      period(['kalinda', true], ['pacific-blue', true], ['wanderer', false], ['runaway-bear', true]));
    expect(noneFree.emptyMessage).toBe('No boats match your filters for these dates.');
  });
});

describe('base hire rate', () => {
  it('shows the selected boat\'s price for the period, or a prompt until both are chosen', () => {
    const results = period(['pacific-blue', true, 4060]);
    expect(baseHireRateText('pacific-blue', results)).toBe('$4,060.00');
    expect(baseHireRateText(null, results)).toBe('Select dates and a boat');
    expect(baseHireRateText('pacific-blue', null)).toBe('Select dates and a boat');
  });
});

describe('rooming warning acceptance', () => {
  const accepted = { slug: 'pacific-blue', adults: 9, children: 1, text: 'This boat has…' };

  it('holds while the boat and guest numbers stay the same', () => {
    expect(roomingAcceptanceHolds(accepted, 'pacific-blue', 9, 1)).toBe(true);
  });

  it('is shown again after the boat or the guests change', () => {
    expect(roomingAcceptanceHolds(null, 'pacific-blue', 9, 1)).toBe(false);
    expect(roomingAcceptanceHolds(accepted, 'kalinda', 9, 1)).toBe(false);
    expect(roomingAcceptanceHolds(accepted, 'pacific-blue', 10, 1)).toBe(false);
    expect(roomingAcceptanceHolds(accepted, 'pacific-blue', 9, 0)).toBe(false);
  });
});

describe('add-ons after changing boat', () => {
  it('removes requested add-ons the new boat does not offer', () => {
    const chosen = new Map([['ice', 2], ['pizza-oven', 1], ['car-parking', 1]]);
    expect(removeDisallowedAddons(chosen, ['ice', 'car-parking', 'bait'])).toEqual(['pizza-oven']);
    expect([...chosen]).toEqual([['ice', 2], ['car-parking', 1]]);
    expect(removeDisallowedAddons(chosen, ['ice', 'car-parking'])).toEqual([]);
  });
});
