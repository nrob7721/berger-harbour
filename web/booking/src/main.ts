import { addDays, addMonths, firstOfMonth, formatLongDate, type IsoDate } from '@shared/dates';
import { $, $$, clear, h, openDialog } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { ApiError } from '@shared/http';
import {
  evaluateFleetDay, type BookingWindow, type DateRange, type DayState, type EnquireOnlyRange, type FleetAvailabilityData,
  type OnlinePeriodType, type Stay,
} from '@shared/periods';
import { phoneField } from '@shared/phone';
import {
  publicApi, type BookingStep, type CreateBookingResult, type PublicBoat, type PublicBookingSettings, type Quote,
} from './api';
import { Calendar } from './calendar';
import { isEmbedded, requestClose } from './embed';
import {
  baseHireRateText, boatList, calendarBoats, chooseBoat, keepChoiceValid, largestBoatGuests, onlySelectedCheckbox,
  removeDisallowedAddons, roomingAcceptanceHolds,
  type BoatCardView, type BoatChoice, type Filters, type FleetBoat, type PeriodBoat, type PeriodResults, type RequestedBoat,
  type RoomingAcceptance,
} from './fleet';
import {
  adultOptions, capacityView, checkInOutText, childOptions, clampChildren, needsRoomingWarning, selectionText, summaryTimes,
} from './rules';
import { mountCheckout } from './stripe';
import { TurnstileWidget } from './turnstile';

type Step = 'loading' | 'dates' | 'details' | 'addons' | 'summary' | 'payment' | 'complete' | 'error';
const FLOW: Step[] = ['dates', 'details', 'addons', 'summary', 'payment'];

/** Which step owns each API validation field, so errors send the customer back to the right place. */
const FIELD_STEP: Record<string, Step> = {
  slug: 'dates',
  periodType: 'dates',
  startDate: 'dates',
  numberOfAdults: 'dates',
  numberOfChildren: 'dates',
  roomingWarningAccepted: 'dates',
  fullName: 'details',
  email: 'details',
  mobile: 'details',
  turnstileToken: 'details',
  draftId: 'details',
  addons: 'addons',
  termsAccepted: 'summary',
};

const state = {
  settings: null as PublicBookingSettings | null,
  /** Active house boats, largest first. */
  fleet: [] as FleetBoat[],
  /** The boat named by `?boat=`, if any. */
  requested: null as RequestedBoat | null,
  periodType: 'Midweek' as OnlinePeriodType,
  selection: null as Stay | null,
  filters: { adults: 1, children: 0, spa: false, petFriendly: false } as Filters,
  choice: { selected: null, onlySelected: false } as BoatChoice,
  /** Results for the selected period, once loaded. */
  period: null as PeriodResults,
  availability: { enquireOnly: [], unavailable: new Map() } as FleetAvailabilityData,
  loadedMonths: new Set<string>(),
  /** Period results per `type|start` and boat details per slug, cached for the session. */
  periodCache: new Map<string, ReadonlyMap<string, PeriodBoat>>(),
  boatCache: new Map<string, Promise<PublicBoat>>(),
  calendarBoats: [] as string[],
  window: { earliestStart: '9999-12-31', bookingsOpenUntil: '0000-01-01' } as BookingWindow,
  rooming: null as RoomingAcceptance | null,
  addons: new Map<string, number>(),
  /** Set when changing boat removed requested add-ons; shown once on the Add-ons step. */
  addonsRemovedOn: null as string | null,
  draftId: null as string | null,
  quote: null as Quote | null,
  booking: null as CreateBookingResult | null,
  paymentReached: false,
  step: 'loading' as Step,
};

let calendar: Calendar;
const turnstile = new TurnstileWidget(() => clearFieldError('turnstileToken'));
const mobile = phoneField({ name: 'mobile', label: 'Mobile number', required: true });

// ---- Step navigation ---------------------------------------------------------------------------------------------

function show(step: Step): void {
  if (step === 'dates' && state.paymentReached) {
    // A hold may have been taken or released since: start from fresh availability and prices.
    state.paymentReached = false;
    refreshAvailability();
  }
  if (step === 'payment') state.paymentReached = true;
  state.step = step;
  for (const section of $$('section.step')) {
    section.classList.toggle('step--active', section.dataset.step === step);
  }
  const current = FLOW.indexOf(step);
  for (const item of $$('#progress li')) {
    const index = FLOW.indexOf(item.dataset.step as Step);
    item.classList.toggle('progress__item--done', current > index || step === 'complete');
    if (index === current) item.setAttribute('aria-current', 'step');
    else item.removeAttribute('aria-current');
  }
  $('#progress').hidden = step === 'loading' || step === 'error';
  const heading = document.querySelector<HTMLElement>(`section[data-step="${step}"] h2`);
  if (heading && step !== 'loading') {
    heading.tabIndex = -1;
    heading.focus({ preventScroll: true });
    window.scrollTo({ top: 0 });
  }
}

function pageNotice(message: string | null, kind: 'error' | 'warning' | 'info' = 'error'): void {
  const el = $('#page-notice');
  clear(el);
  if (message) el.append(h('div', { class: `notice notice--${kind}` }, message));
}

function contactText(): string {
  const s = state.settings!;
  return `${s.contactPhone} / ${s.contactEmail}`;
}

function boatName(slug: string | null): string | null {
  if (!slug) return null;
  return state.fleet.find((b) => b.slug === slug)?.name ?? (state.requested?.slug === slug ? state.requested.name : null);
}

/** The selected boat's details (beds, deposit, add-ons), fetched once per boat. */
function boatDetails(slug: string): Promise<PublicBoat> {
  let details = state.boatCache.get(slug);
  if (!details) {
    details = publicApi.boat(slug);
    details.catch(() => state.boatCache.delete(slug));
    state.boatCache.set(slug, details);
  }
  return details;
}

// ---- Booking draft -----------------------------------------------------------------------------------------------

const DRAFT_KEY = 'bh-booking-draft';

function rememberDraft(draftId: string | null): void {
  state.draftId = draftId;
  try {
    if (draftId) sessionStorage.setItem(DRAFT_KEY, draftId);
    else sessionStorage.removeItem(DRAFT_KEY);
  } catch {
    // storage may be unavailable (private mode, blocked cookies); the draft then lives for this page only
  }
}

function recallDraft(): string | null {
  try {
    return sessionStorage.getItem(DRAFT_KEY);
  } catch {
    return null;
  }
}

function addonRequests() {
  return [...state.addons].map(([addonId, quantity]) => ({ addonId, quantity }));
}

/** Records progress on the draft (moving forward a step), including a change of boat. Failures never block the customer. */
async function saveDraft(step: BookingStep): Promise<void> {
  const slug = state.choice.selected;
  if (!state.draftId || !state.selection || !slug) return;
  const d = details();
  try {
    await publicApi.updateDraft(state.draftId, {
      slug,
      periodType: state.periodType,
      startDate: state.selection.start,
      numberOfAdults: state.filters.adults,
      numberOfChildren: state.filters.children,
      fullName: d.fullName,
      email: d.email,
      mobile: d.mobile ?? null,
      addons: addonRequests(),
      lastStepReached: step,
    });
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) rememberDraft(null);
  }
}

// ---- Loading -----------------------------------------------------------------------------------------------------

async function init(): Promise<void> {
  const slug = (new URLSearchParams(location.search).get('boat') ?? '').trim().toLowerCase() || null;
  try {
    const [fleet, requested] = await Promise.all([publicApi.fleet(), slug ? boatDetails(slug) : null]);
    state.settings = fleet.settings;
    state.fleet = fleet.boats;
    if (requested) {
      state.requested = {
        slug: requested.slug, name: requested.name, imageUrl: requested.imageUrl, maxNoOfGuests: requested.maxNoOfGuests,
        hasSpa: requested.hasSpa, isPetFriendly: requested.isPetFriendly, isActive: requested.isActive,
      };
      if (requested.isActive) state.choice = { selected: requested.slug, onlySelected: true };
    }
  } catch (e) {
    fatal(e instanceof ApiError && e.status === 404
      ? "Sorry, we couldn't find that boat. Please go back and choose a boat from our website."
      : 'Sorry, the booking system is not available right now. Please try again shortly.');
    return;
  }

  const settings = state.settings;
  ($('#terms-link') as HTMLAnchorElement).href = settings.hireTermsUrl;
  state.window = {
    earliestStart: addDays(settings.today, settings.minimumLeadTimeDays),
    bookingsOpenUntil: settings.bookingsOpenUntil,
  };
  state.draftId = recallDraft();

  calendar = new Calendar({
    container: $('#calendar'),
    minMonth: state.window.earliestStart,
    maxMonth: addDays(settings.bookingsOpenUntil, 1),
    dayState,
    selection: () => state.selection,
    onActivate,
    onMonthChange: (month) => {
      updateTimes();
      void loadAvailability(month);
    },
  });

  setUpDates();
  setUpDetails();
  setUpAddons();
  setUpSummary();
  renderDatesStep();
  show('dates');
  await loadAvailability(calendar.visibleMonth);
}

function fatal(message: string): void {
  $('#fatal-error').textContent = message;
  $('#app-title').textContent = 'Book a houseboat';
  show('error');
}

// ---- Dates & boat ------------------------------------------------------------------------------------------------

function dayState(day: IsoDate): DayState {
  if (!state.loadedMonths.has(firstOfMonth(day))) {
    return { status: 'not-in-mode', stay: null, blockedName: null, insideBlockedPeriod: false, clickable: false };
  }
  return evaluateFleetDay(state.periodType, day, state.availability, state.calendarBoats, state.window);
}

/**
 * Fetches every active boat's availability for a visible month (one request), padded so periods crossing the month
 * edges are evaluated correctly. Months already loaded are not fetched again.
 */
async function loadAvailability(month: IsoDate, force = false): Promise<void> {
  const key = firstOfMonth(month);
  if (state.loadedMonths.has(key) && !force) return;
  $('#calendar').setAttribute('aria-busy', 'true');
  try {
    const result = await publicApi.availability(addDays(key, -7), addDays(addMonths(key, 1), 14));
    const unavailable = new Map(state.availability.unavailable);
    for (const boat of result.boats) {
      const ranges = new Map((unavailable.get(boat.slug) ?? []).map((r) => [`${r.start}|${r.end}`, r]));
      for (const r of boat.unavailable) ranges.set(`${r.start}|${r.end}`, r);
      unavailable.set(boat.slug, [...ranges.values()] as DateRange[]);
    }
    const enquire = new Map(state.availability.enquireOnly.map((r) => [`${r.firstNight}|${r.name}`, r]));
    for (const r of result.enquireOnly) enquire.set(`${r.firstNight}|${r.name}`, r);
    state.availability = { unavailable, enquireOnly: [...enquire.values()] as EnquireOnlyRange[] };
    state.loadedMonths.add(key);
  } catch (e) {
    pageNotice(e instanceof ApiError && e.status === 429 ? e.messages.join(' ') : 'We could not load availability. Please try again.');
  } finally {
    $('#calendar').removeAttribute('aria-busy');
    calendar.render();
  }
}

/** Forgets cached availability and period results (after a 409, or coming back from Payment) and loads them again. */
function refreshAvailability(): void {
  state.availability = { enquireOnly: [], unavailable: new Map() };
  state.loadedMonths.clear();
  state.periodCache.clear();
  void loadAvailability(calendar.visibleMonth, true);
  void loadPeriod();
}

/** "Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)" for the selected dates, or the visible month. */
function updateTimes(): void {
  const s = state.settings!;
  const from = state.selection?.start ?? calendar?.visibleMonth ?? s.today;
  $('#times').textContent = checkInOutText(s, from, state.selection?.end ?? from);
}

function setUpDates(): void {
  for (const radio of $$<HTMLInputElement>('#period-options input')) {
    radio.addEventListener('change', () => {
      state.periodType = radio.value as OnlinePeriodType;
      setSelection(null); // the selected boat is kept
    });
  }

  const adults = $<HTMLSelectElement>('#adults');
  const children = $<HTMLSelectElement>('#children');
  for (const n of adultOptions(largestBoatGuests(state.fleet))) adults.append(h('option', { value: n, selected: n === 1 }, String(n)));
  renderChildOptions();
  adults.addEventListener('change', () => {
    state.filters.adults = Number(adults.value) || 1;
    renderChildOptions();
    filtersChanged();
  });
  children.addEventListener('change', () => {
    state.filters.children = Number(children.value) || 0;
    filtersChanged();
  });
  $<HTMLInputElement>('#filter-spa').addEventListener('change', (e) => {
    state.filters.spa = (e.target as HTMLInputElement).checked;
    filtersChanged();
  });
  $<HTMLInputElement>('#filter-pet').addEventListener('change', (e) => {
    state.filters.petFriendly = (e.target as HTMLInputElement).checked;
    filtersChanged();
  });
  $<HTMLInputElement>('#only-selected').addEventListener('change', (e) => {
    state.choice = { ...state.choice, onlySelected: (e.target as HTMLInputElement).checked };
    renderDatesStep();
  });

  $('section[data-step="dates"] [data-next]').addEventListener('click', () => void continueFromDates());
}

/** Children: 0 … N − adults, where N is the largest boat; changing adults clamps the children already chosen. */
function renderChildOptions(): void {
  const max = largestBoatGuests(state.fleet);
  const select = $<HTMLSelectElement>('#children');
  state.filters.children = clampChildren(max, state.filters.adults, state.filters.children);
  clear(select);
  for (const n of childOptions(max, state.filters.adults)) {
    select.append(h('option', { value: n, selected: n === state.filters.children }, String(n)));
  }
}

function filtersChanged(): void {
  clearFieldError('numberOfAdults');
  clearFieldError('numberOfChildren');
  keepSelectionValid();
  renderDatesStep();
}

function onActivate(_day: IsoDate, dayInfo: DayState): void {
  if (dayInfo.status === 'enquire') {
    void openDialog({
      title: 'Please contact us to book these dates',
      body: `Bookings over ${dayInfo.blockedName} must be made by phone or email: ${contactText()}.`,
      actions: [{ label: 'OK', value: 'ok', primary: true }],
    });
    return;
  }
  if (dayInfo.stay) setSelection(dayInfo.stay);
}

function setSelection(stay: Stay | null): void {
  state.selection = stay;
  state.quote = null;
  $('#selection-summary').textContent = stay ? selectionText(stay.start, stay.end) : '';
  clearFieldError('startDate');
  void loadPeriod();
}

const periodKey = (type: OnlinePeriodType, stay: Stay) => `${type}|${stay.start}`;

/** Each boat's availability and price for the selected period: cached, otherwise one request. */
async function loadPeriod(): Promise<void> {
  const stay = state.selection;
  state.period = stay ? state.periodCache.get(periodKey(state.periodType, stay)) ?? null : null;
  keepSelectionValid();
  renderDatesStep();
  if (!stay || state.period) return;

  const type = state.periodType;
  try {
    const result = await publicApi.periodAvailability(type, stay.start);
    const boats = new Map(result.boats.map((b) => [b.slug, b]));
    state.periodCache.set(periodKey(type, stay), boats);
    if (state.selection !== stay || state.periodType !== type) return; // the customer has moved on
    state.period = boats;
    keepSelectionValid();
    renderDatesStep();
  } catch (e) {
    if (state.selection !== stay) return;
    if (e instanceof ApiError && e.status === 400) {
      setSelection(null);
      pageNotice(e.messages.join(' '));
    } else {
      pageNotice(e instanceof ApiError && e.status === 429 ? e.messages.join(' ') : 'We could not load prices for these dates. Please try again.');
    }
  }
}

/** A change made the selected boat ineligible: deselect it (and untick the checkbox), keeping the period. */
function keepSelectionValid(): void {
  const before = state.choice.selected;
  state.choice = keepChoiceValid(state.choice, state.fleet, state.filters, state.period);
  if (before && !state.choice.selected) {
    $('#boat-announcer').textContent = `${boatName(before)} no longer matches your choices and has been deselected.`;
  }
}

function selectBoat(slug: string): void {
  if (state.choice.selected === slug) return;
  state.choice = chooseBoat(state.choice, slug);
  clearFieldError('slug');
  $('#boat-announcer').textContent = `${boatName(slug)} selected.`;
  renderDatesStep();
}

/** Redraws everything on the step that depends on the period, guests, filters and selected boat. */
function renderDatesStep(): void {
  const { choice, filters } = state;
  const selectedName = boatName(choice.selected);
  const title = selectedName ? `Book ${selectedName}` : 'Book a houseboat';
  $('#app-title').textContent = title;
  document.title = `${title} — Berger Houseboats`;

  const checkbox = onlySelectedCheckbox(selectedName, choice.onlySelected);
  const input = $<HTMLInputElement>('#only-selected');
  input.checked = checkbox.checked;
  input.disabled = checkbox.disabled;
  $('#only-selected-label').textContent = checkbox.label;

  state.calendarBoats = calendarBoats(state.fleet, filters, choice.selected, choice.onlySelected);
  updateTimes();
  calendar.render();
  renderBoats();
  $('#base-rate').textContent = baseHireRateText(choice.selected, state.period);
  $<HTMLButtonElement>('section[data-step="dates"] [data-next]').disabled = !state.selection || !choice.selected || !state.period;
}

const PLACEHOLDER = () => h('div', { class: 'boat-card__image boat-card__image--placeholder', 'aria-hidden': 'true' });

/** One image element per boat, reused across re-renders so images don't reload or flicker. */
const boatImages = new Map<string, HTMLElement>();

function boatImage(boat: FleetBoat): HTMLElement {
  let image = boatImages.get(boat.slug);
  if (!image) {
    if (boat.imageUrl) {
      const img = h('img', { class: 'boat-card__image', src: boat.imageUrl, alt: '', loading: 'lazy', decoding: 'async' });
      img.addEventListener('error', () => {
        const placeholder = PLACEHOLDER();
        boatImages.set(boat.slug, placeholder);
        img.replaceWith(placeholder);
      }, { once: true });
      image = img;
    } else {
      image = PLACEHOLDER();
    }
    boatImages.set(boat.slug, image);
  }
  return image;
}

/** The boat cards: a single-select radio group (arrow keys move and select, Enter/Space select). */
function renderBoats(): void {
  const list = $('#boat-list');
  const focusedSlug = list.contains(document.activeElement) ? (document.activeElement as HTMLElement).dataset.slug : undefined;
  clear(list);
  const view = boatList(state.fleet, state.requested, state.filters, state.period);
  const cards = [view.requested, ...view.others].filter((c): c is BoatCardView => !!c);
  const selectable = cards.filter((c) => !c.reason).map((c) => c.boat.slug);
  const tabStop = selectable.includes(state.choice.selected ?? '') ? state.choice.selected : selectable[0];

  const card = (c: BoatCardView) => {
    const { boat } = c;
    const selected = boat.slug === state.choice.selected;
    const el = h('div', {
      class: `boat-card${selected ? ' boat-card--selected' : ''}${c.reason ? ' boat-card--disabled' : ''}`,
      role: 'radio',
      'aria-checked': selected ? 'true' : 'false',
      'aria-disabled': c.reason ? 'true' : null,
      tabindex: boat.slug === tabStop ? 0 : -1,
      'data-slug': boat.slug,
    },
      boatImage(boat),
      h('div', { class: 'boat-card__body' },
        h('strong', { class: 'boat-card__name' }, boat.name),
        h('span', { class: 'boat-card__guests' }, `Up to ${boat.maxNoOfGuests} guests`),
        boat.hasSpa || boat.isPetFriendly
          ? h('span', { class: 'boat-card__badges' },
            boat.hasSpa ? h('span', { class: 'badge' }, 'Spa') : null,
            boat.isPetFriendly ? h('span', { class: 'badge' }, 'Pet friendly') : null)
          : null,
        c.hirePrice !== null ? h('span', { class: 'boat-card__price' }, formatMoney(c.hirePrice)) : null,
        c.reason ? h('span', { class: 'boat-card__reason' }, c.reason) : null,
      ),
      h('span', { class: 'boat-card__tick', 'aria-hidden': 'true' }),
    );
    if (!c.reason) {
      el.addEventListener('click', () => selectBoat(boat.slug));
    }
    el.addEventListener('keydown', (e) => onBoatKey(e, boat.slug, selectable));
    return el;
  };

  if (view.requested) list.append(card(view.requested));
  if (view.separator) list.append(h('p', { class: 'boat-list__separator' }, 'Other boats available'));
  for (const c of view.others) list.append(card(c));
  if (view.emptyMessage) list.append(h('p', { class: 'boat-list__empty muted' }, view.emptyMessage));

  if (focusedSlug) {
    (list.querySelector<HTMLElement>(`[data-slug="${CSS.escape(focusedSlug)}"]`) ?? list.querySelector<HTMLElement>('[tabindex="0"]'))?.focus();
  }
}

function onBoatKey(e: KeyboardEvent, slug: string, selectable: string[]): void {
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault();
    if (selectable.includes(slug)) selectBoat(slug);
    return;
  }
  const step = e.key === 'ArrowDown' || e.key === 'ArrowRight' ? 1 : e.key === 'ArrowUp' || e.key === 'ArrowLeft' ? -1 : 0;
  if (step === 0 || selectable.length === 0) return;
  e.preventDefault();
  const index = selectable.indexOf(slug);
  const next = selectable[(index + step + selectable.length) % selectable.length]!;
  $(`#boat-list [data-slug="${CSS.escape(next)}"]`).focus();
  selectBoat(next);
}

/** Continue: shows the rooming warning when needed, drops add-ons the boat doesn't offer, and records the draft. */
async function continueFromDates(): Promise<void> {
  const slug = state.choice.selected;
  if (!state.selection || !slug || !state.period) return;
  const button = $<HTMLButtonElement>('section[data-step="dates"] [data-next]');
  button.disabled = true;
  try {
    const boat = await boatDetails(slug);
    const { adults, children } = state.filters;
    if (needsRoomingWarning(adults, children, boat.adultBeds, boat.kidsBeds) &&
        !roomingAcceptanceHolds(state.rooming, slug, adults, children) &&
        !(await confirmRooming(slug, adults, children))) {
      return;
    }
    if (removeDisallowedAddons(state.addons, boat.addons.map((a) => a.id)).length > 0) state.addonsRemovedOn = boat.name;
    if (state.draftId) void saveDraft('Details');
    pageNotice(null);
    show('details');
  } catch (e) {
    handleBookingError(e);
  } finally {
    renderDatesStep();
  }
}

/** Shows the rooming warning (text generated by the server) and records the exact text accepted. */
async function confirmRooming(slug: string, adults: number, children: number): Promise<boolean> {
  const quote = await publicApi.quote(slug, {
    periodType: state.periodType,
    startDate: state.selection!.start,
    numberOfAdults: adults,
    numberOfChildren: children,
    addons: [],
  });
  const text = quote.roomingWarningText;
  if (!text) return true;
  const choice = await openDialog({
    title: 'Please check the sleeping arrangements',
    body: h('p', {}, text),
    actions: [
      { label: 'Cancel', value: 'cancel' },
      { label: 'Accept', value: 'accept', primary: true },
    ],
    dismissValue: 'cancel',
  });
  if (choice !== 'accept') return false;
  state.rooming = { slug, adults, children, text };
  return true;
}

// ---- Your details ------------------------------------------------------------------------------------------------

function setUpDetails(): void {
  $('#mobile-field').replaceWith(mobile.element);
  const section = $('section[data-step="details"]');
  new MutationObserver(() => {
    if (section.classList.contains('step--active')) {
      turnstile.render($('#turnstile')).catch(() => setFieldError('turnstileToken', 'The security check could not load. Please refresh the page.'));
    }
  }).observe(section, { attributes: true, attributeFilter: ['class'] });

  for (const input of $$<HTMLInputElement>('#details-form input')) {
    input.addEventListener('input', () => clearFieldError(input.closest<HTMLElement>('[data-field]')?.dataset.field ?? ''));
  }
  $('[data-back]', section).addEventListener('click', () => show('dates'));
  $('[data-next]', section).addEventListener('click', () => void continueFromDetails());
}

function details() {
  return {
    fullName: $<HTMLInputElement>('#fullName').value.trim(),
    email: $<HTMLInputElement>('#email').value.trim(),
    mobile: mobile.value(),
  };
}

function validateDetails(needsTurnstile: boolean): boolean {
  const d = details();
  const errors: [string, string][] = [];
  if (!d.fullName) errors.push(['fullName', 'Please enter your full name.']);
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(d.email)) errors.push(['email', 'Please enter a valid email address.']);
  if (d.mobile === null) errors.push(['mobile', 'Please enter your mobile number.']);
  else if (d.mobile === undefined) errors.push(['mobile', 'Please enter a valid mobile number for the selected country.']);
  if (needsTurnstile && !turnstile.value) errors.push(['turnstileToken', 'Please complete the security check.']);
  for (const field of ['fullName', 'email', 'mobile', 'turnstileToken']) clearFieldError(field);
  for (const [field, message] of errors) setFieldError(field, message);
  if (errors.length > 0) {
    document.querySelector<HTMLElement>(`[data-field="${errors[0]![0]}"] input:not(.phone-search)`)?.focus();
  }
  return errors.length === 0;
}

/** Creates the booking draft the first time (Turnstile checked then), afterwards updates it. */
async function continueFromDetails(): Promise<void> {
  if (!validateDetails(!state.draftId)) return;
  const button = $<HTMLButtonElement>('section[data-step="details"] [data-next]');
  button.disabled = true;
  try {
    if (state.draftId) {
      await saveDraft('Addons');
    }
    if (!state.draftId) {
      // No draft yet, or the old one was completed or deleted.
      if (!validateDetails(true)) return;
      const d = details();
      const created = await publicApi.createDraft({
        slug: state.choice.selected!,
        periodType: state.periodType,
        startDate: state.selection!.start,
        numberOfAdults: state.filters.adults,
        numberOfChildren: state.filters.children,
        fullName: d.fullName,
        email: d.email,
        mobile: d.mobile!,
        turnstileToken: turnstile.value,
      });
      rememberDraft(created.draftId);
      turnstile.reset();
      void saveDraft('Addons');
    }
    pageNotice(null);
    show('addons');
  } catch (e) {
    turnstile.reset();
    handleBookingError(e);
  } finally {
    button.disabled = false;
  }
}

function setFieldError(field: string, message: string): void {
  const wrapper = document.querySelector<HTMLElement>(`[data-field="${field}"]`);
  if (!wrapper) return;
  clearFieldError(field);
  wrapper.classList.add('field--invalid');
  const id = `${field}-error`;
  wrapper.append(h('span', { class: 'field__error', id }, message));
  const control = wrapper.querySelector('input[name], select, input');
  control?.setAttribute('aria-describedby', id);
  control?.setAttribute('aria-invalid', 'true');
}

function clearFieldError(field: string): void {
  const wrapper = document.querySelector<HTMLElement>(`[data-field="${field}"]`);
  if (!wrapper) return;
  wrapper.classList.remove('field--invalid');
  wrapper.querySelector('.field__error')?.remove();
  wrapper.querySelector('[aria-invalid]')?.removeAttribute('aria-invalid');
}

// ---- Add-ons -----------------------------------------------------------------------------------------------------

function setUpAddons(): void {
  const section = $('section[data-step="addons"]');
  $('[data-back]', section).addEventListener('click', () => show('details'));
  $('[data-next]', section).addEventListener('click', () => {
    void saveDraft('Summary');
    void goToSummary();
  });
  // Prices depend on the period type and capacity on the dates, so the list is rebuilt whenever the step is shown.
  new MutationObserver(() => {
    if (section.classList.contains('step--active')) void renderAddons();
  }).observe(section, { attributes: true, attributeFilter: ['class'] });
}

function addonPrice(prices: { midweek: number; weekend: number; week: number } | null): number | null {
  if (!prices) return null;
  return state.periodType === 'Midweek' ? prices.midweek : state.periodType === 'Weekend' ? prices.weekend : prices.week;
}

/** Remaining capacity of the capacity-limited add-ons for the selected stay, fetched fresh every time. */
async function loadRemaining(addons: PublicBoat['addons']): Promise<Map<string, number>> {
  const stay = state.selection!;
  const remaining = new Map<string, number>();
  await Promise.all(addons.filter((a) => a.hasCapacity).map(async (a) => {
    try {
      remaining.set(a.id, (await publicApi.addonAvailability(a.id, stay.start, stay.end)).remaining);
    } catch {
      // shown without a limit; the server still enforces it
    }
  }));
  return remaining;
}

let addonRender = 0;

async function renderAddons(): Promise<void> {
  const render = ++addonRender; // the step can be activated again while figures load; only the newest render may draw
  const list = $('#addon-list');
  const removed = $('#addons-removed');
  removed.hidden = !state.addonsRemovedOn;
  removed.textContent = state.addonsRemovedOn
    ? `Some add-ons you chose aren't offered on ${state.addonsRemovedOn} and have been removed.`
    : '';
  state.addonsRemovedOn = null;
  const boat = await boatDetails(state.choice.selected!);
  const addons = boat.addons;
  let remaining = new Map<string, number>();
  if (addons.some((a) => a.hasCapacity)) {
    list.setAttribute('aria-busy', 'true');
    remaining = await loadRemaining(addons);
    if (render !== addonRender) return;
    list.setAttribute('aria-busy', 'false');
  }
  if (render !== addonRender) return;
  clear(list);
  const shown = addons.map((addon) => ({ addon, view: capacityView(addon, remaining.get(addon.id)) }))
    .filter(({ addon, view }) => {
      if (view.hidden) state.addons.delete(addon.id); // fully booked for these dates: not offered at all
      return !view.hidden;
    });
  if (shown.length === 0) {
    list.append(h('li', { class: 'muted' }, 'No add-ons are available for this boat.'));
    return;
  }
  for (const { addon, view } of shown) {
    const id = `addon-${addon.id}`;
    const price = addonPrice(addon.prices);
    const checked = state.addons.has(addon.id);
    if (checked && state.addons.get(addon.id)! > view.max) state.addons.set(addon.id, view.max);
    const quantity = h('input', {
      type: 'number', min: 1, max: view.max, value: state.addons.get(addon.id) ?? 1, id: `${id}-qty`,
      class: 'addon__qty', 'aria-label': `Quantity of ${addon.name}`, disabled: !checked,
    });
    const checkbox = h('input', { type: 'checkbox', id, checked });
    checkbox.addEventListener('change', () => {
      if (checkbox.checked) state.addons.set(addon.id, Math.min(view.max, Math.max(1, Number(quantity.value) || 1)));
      else state.addons.delete(addon.id);
      quantity.disabled = !checkbox.checked;
      clearFieldError('addons');
    });
    quantity.addEventListener('change', () => {
      const n = Math.min(view.max, Math.max(1, Math.round(Number(quantity.value) || 1)));
      quantity.value = String(n);
      if (checkbox.checked) state.addons.set(addon.id, n);
    });
    list.append(h('li', { class: 'addon' },
      checkbox,
      h('label', { for: id, class: 'addon__label' },
        h('strong', {}, addon.name),
        addon.description ? h('span', { class: 'muted' }, addon.description) : null,
        view.text ? h('span', { class: 'addon__remaining' }, view.text) : null),
      h('span', { class: 'addon__price' },
        addon.priceOnRequest || price === null ? 'Price on request' : `${formatMoney(price)}${addon.quantityApplies ? ' each' : ''}`),
      addon.quantityApplies ? quantity : null,
    ));
  }
}

// ---- Summary -----------------------------------------------------------------------------------------------------

function setUpSummary(): void {
  const section = $('section[data-step="summary"]');
  const terms = $<HTMLInputElement>('#terms');
  terms.addEventListener('change', () => {
    clearFieldError('termsAccepted');
    $<HTMLButtonElement>('#pay-button').disabled = !terms.checked;
  });
  $('[data-back]', section).addEventListener('click', () => show('addons'));
  $('#pay-button').addEventListener('click', () => void pay());
}

async function goToSummary(): Promise<void> {
  const button = $<HTMLButtonElement>('section[data-step="addons"] [data-next]');
  button.disabled = true;
  try {
    state.quote = await publicApi.quote(state.choice.selected!, {
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfAdults: state.filters.adults,
      numberOfChildren: state.filters.children,
      addons: addonRequests(),
    });
    renderSummary(state.quote);
    pageNotice(null);
    show('summary');
  } catch (e) {
    handleBookingError(e);
  } finally {
    button.disabled = false;
  }
}

function renderSummary(quote: Quote): void {
  const container = $('#summary');
  clear(container);
  const row = (label: string, value: Node | string) => h('div', { class: 'summary__row' }, h('dt', {}, label), h('dd', {}, value));
  const addons = quote.addons.length === 0
    ? 'None'
    : h('ul', { class: 'plain-list' }, ...quote.addons.map((a) => h('li', {},
      `${a.name}${a.quantity > 1 ? ` × ${a.quantity}` : ''} — ${a.lineTotal !== null ? formatMoney(a.lineTotal) : 'price on request'}`)));
  const schedule = h('ul', { class: 'plain-list' }, ...quote.milestones.map((m) => h('li', {},
    `${m.label}: ${formatMoney(m.amount)}` +
    (m.kind === 'Deposit' ? '' : m.dueNow ? ' — due now; we will email you a payment link' : m.dueDate ? ` by ${formatLongDate(m.dueDate)}` : ''))));
  const times = summaryTimes(quote, quote.startDate, quote.endDate);
  const guests = `${quote.numberOfAdults} adult${quote.numberOfAdults === 1 ? '' : 's'}, ` +
    `${quote.numberOfChildren} child${quote.numberOfChildren === 1 ? '' : 'ren'} (under 12)`;

  container.append(
    h('dl', { class: 'summary' },
      row('Boat', quote.boatName),
      row('Check-in', times.checkIn),
      row('Check-out', times.checkOut),
      row('Guests', guests),
      row('Season', quote.seasonName),
      row('Hire price', formatMoney(quote.hirePrice)),
      row('Requested add-ons', addons),
      row('Total', formatMoney(quote.totalPrice)),
      row('Payment schedule', schedule),
    ),
    h('p', { class: 'muted', hidden: quote.addons.length === 0 },
      'Add-ons are requests and will be confirmed by our staff. Add-ons priced "on request" are added to your balance once confirmed.'),
    h('p', { class: 'summary__due' }, 'Amount payable now (deposit): ', h('strong', {}, formatMoney(quote.amountDueAtCheckout))),
    h('p', { class: 'notice' }, `A ${formatMoney(quote.securityBond)} security bond applies — details will be provided.`),
  );
  $('#pay-button').textContent = `Pay ${formatMoney(quote.amountDueAtCheckout)}`;
  $<HTMLButtonElement>('#pay-button').disabled = !$<HTMLInputElement>('#terms').checked;
}

// ---- Payment -----------------------------------------------------------------------------------------------------

async function pay(): Promise<void> {
  const button = $<HTMLButtonElement>('#pay-button');
  if (!$<HTMLInputElement>('#terms').checked) {
    setFieldError('termsAccepted', 'Please accept the Hire Terms and Procedures.');
    return;
  }
  if (!state.draftId) {
    show('details');
    pageNotice('Please confirm your details again.');
    return;
  }
  button.disabled = true;
  const d = details();
  const slug = state.choice.selected!;
  const { adults, children } = state.filters;
  const rooming = roomingAcceptanceHolds(state.rooming, slug, adults, children) ? state.rooming : null;
  try {
    state.booking = await publicApi.createBooking({
      draftId: state.draftId,
      slug,
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfAdults: adults,
      numberOfChildren: children,
      fullName: d.fullName,
      email: d.email,
      mobile: d.mobile ?? null,
      addons: addonRequests(),
      roomingWarningAccepted: !!rooming,
      roomingWarningText: rooming?.text ?? null,
      termsAccepted: true,
    });
  } catch (e) {
    handleBookingError(e);
    return;
  } finally {
    button.disabled = false;
  }

  $('#payment-hold').textContent =
    `Booking ${state.booking.reference}: your dates are held while you pay. Please complete payment within 30 minutes.`;
  show('payment');
  try {
    await mountCheckout($('#checkout'), state.booking.clientSecret);
    rememberDraft(null);
    show('complete');
    void confirmCompletion();
  } catch (e) {
    pageNotice((e as Error).message);
  }
}

function handleBookingError(e: unknown): void {
  if (!(e instanceof ApiError)) {
    pageNotice('Something went wrong. Please try again.');
    return;
  }
  if (e.status === 409) {
    // Keep the period, guests and filters; the boat is now taken for them, so the customer can choose another.
    state.choice = { selected: null, onlySelected: false };
    refreshAvailability();
    show('dates');
    pageNotice('Sorry, those dates were just taken.');
    return;
  }
  const fields = Object.keys(e.fieldErrors);
  if (fields.includes('draftId')) rememberDraft(null);
  const firstStep = FLOW.find((s) => fields.some((f) => FIELD_STEP[f] === s));
  for (const [field, messages] of Object.entries(e.fieldErrors)) setFieldError(field, messages.join(' '));
  if (firstStep) show(firstStep);
  pageNotice(e.messages.join(' '));
}

// ---- Completion --------------------------------------------------------------------------------------------------

async function confirmCompletion(): Promise<void> {
  const booking = state.booking!;
  const container = $('#complete');
  const email = details().email;
  clear(container);
  container.append(h('p', {}, 'Confirming your booking…'));
  pageNotice(null);
  if (isEmbedded) $('#close-button').hidden = false;

  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    try {
      const status = await publicApi.status(booking.reference, booking.sessionId);
      if (status.status === 'Active') {
        clear(container);
        container.append(
          h('div', { class: 'notice notice--success' },
            h('p', {}, 'Your booking is confirmed. Your reference is ', h('strong', {}, status.reference), '.'),
            h('p', {}, `A confirmation email has been sent to ${status.email}.`)),
        );
        return;
      }
      if (status.status === 'Expired') {
        clear(container);
        container.append(h('div', { class: 'notice notice--warning' },
          `We received your payment for ${status.reference}, but your dates could not be held. Our team has been notified and will contact you. ` +
          `You can also reach us on ${contactText()}.`));
        return;
      }
    } catch {
      // keep polling
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
  clear(container);
  container.append(h('div', { class: 'notice' },
    h('p', {}, 'Thank you — we have received your payment and are confirming booking ', h('strong', {}, booking.reference), '.'),
    h('p', {}, `A confirmation email will be sent to ${email} shortly.`)));
}

$('#close-button').addEventListener('click', requestClose);

void init();
