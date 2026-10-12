import { addDays, addMonths, firstOfMonth, formatLongDate, type IsoDate } from '@shared/dates';
import { $, $$, clear, h, openDialog } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { ApiError } from '@shared/http';
import { evaluateDay, type AvailabilityData, type BookingWindow, type DayState, type OnlinePeriodType, type Stay } from '@shared/periods';
import { phoneField } from '@shared/phone';
import { publicApi, type BookingStep, type CreateBookingResult, type PublicBoat, type Quote } from './api';
import { Calendar } from './calendar';
import { isEmbedded, requestClose } from './embed';
import { adultOptions, capacityView, checkInOutText, childOptions, clampChildren, needsRoomingWarning } from './rules';
import { mountCheckout } from './stripe';
import { TurnstileWidget } from './turnstile';

type Step = 'loading' | 'dates' | 'details' | 'guests' | 'addons' | 'summary' | 'payment' | 'complete' | 'error';
const FLOW: Step[] = ['dates', 'details', 'guests', 'addons', 'summary', 'payment'];

/** Which step owns each API validation field, so errors send the customer back to the right place. */
const FIELD_STEP: Record<string, Step> = {
  periodType: 'dates',
  startDate: 'dates',
  fullName: 'details',
  email: 'details',
  mobile: 'details',
  turnstileToken: 'details',
  draftId: 'details',
  numberOfAdults: 'guests',
  numberOfChildren: 'guests',
  roomingWarningAccepted: 'guests',
  addons: 'addons',
  termsAccepted: 'summary',
};

const state = {
  slug: '',
  boat: null as PublicBoat | null,
  periodType: 'Midweek' as OnlinePeriodType,
  selection: null as Stay | null,
  availability: { unavailable: [], enquireOnly: [] } as AvailabilityData,
  loadedMonths: new Set<string>(),
  window: { earliestStart: '9999-12-31', bookingsOpenUntil: '0000-01-01' } as BookingWindow,
  adults: null as number | null,
  children: 0,
  roomingText: null as string | null,
  addons: new Map<string, number>(),
  /** What is left of each capacity-limited add-on for `remainingFor` (the selected stay). */
  remaining: new Map<string, number>(),
  remainingFor: null as string | null,
  draftId: null as string | null,
  quote: null as Quote | null,
  booking: null as CreateBookingResult | null,
  step: 'loading' as Step,
};

let calendar: Calendar;
const turnstile = new TurnstileWidget(() => clearFieldError('turnstileToken'));
const mobile = phoneField({ name: 'mobile', label: 'Mobile number', required: true });

// ---- Step navigation ---------------------------------------------------------------------------------------------

function show(step: Step): void {
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
  const s = state.boat!.settings;
  return `${s.contactPhone} / ${s.contactEmail}`;
}

// ---- Booking draft -----------------------------------------------------------------------------------------------

const draftKey = () => `bh-booking-draft:${state.slug}`;

function rememberDraft(draftId: string | null): void {
  state.draftId = draftId;
  try {
    if (draftId) sessionStorage.setItem(draftKey(), draftId);
    else sessionStorage.removeItem(draftKey());
  } catch {
    // storage may be unavailable (private mode, blocked cookies); the draft then lives for this page only
  }
}

function recallDraft(): string | null {
  try {
    return sessionStorage.getItem(draftKey());
  } catch {
    return null;
  }
}

function addonRequests() {
  return [...state.addons].map(([addonId, quantity]) => ({ addonId, quantity }));
}

/** Records progress on the draft (moving forward a step). Failures never block the customer. */
async function saveDraft(step: BookingStep): Promise<void> {
  if (!state.draftId || !state.selection) return;
  const d = details();
  try {
    await publicApi.updateDraft(state.draftId, {
      periodType: state.periodType,
      startDate: state.selection.start,
      fullName: d.fullName,
      email: d.email,
      mobile: d.mobile ?? null,
      numberOfAdults: state.adults,
      numberOfChildren: state.adults === null ? null : state.children,
      addons: addonRequests(),
      lastStepReached: step,
    });
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) rememberDraft(null);
  }
}

// ---- Loading -----------------------------------------------------------------------------------------------------

async function init(): Promise<void> {
  state.slug = (new URLSearchParams(location.search).get('boat') ?? '').trim().toLowerCase();
  if (!state.slug) {
    fatal('No boat was chosen. Please go back to the boat page and press Book Now.');
    return;
  }

  try {
    state.boat = await publicApi.boat(state.slug);
  } catch (e) {
    fatal(e instanceof ApiError && e.status === 404
      ? "Sorry, we couldn't find that boat. Please go back and choose a boat from our website."
      : 'Sorry, the booking system is not available right now. Please try again shortly.');
    return;
  }

  const boat = state.boat;
  const settings = boat.settings;
  document.title = `Book ${boat.name} — Berger Houseboats`;
  $('#boat-name').textContent = `Book ${boat.name}`;
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
  setUpGuests();
  setUpAddons();
  setUpSummary();
  updateTimes();
  show('dates');

  if (!boat.isActive) {
    pageNotice(`This boat is currently unavailable for online booking. Please contact us on ${contactText()}.`, 'warning');
    document.getElementById('app')!.classList.add('app--inactive');
    for (const input of $$<HTMLInputElement>('#period-options input')) input.disabled = true;
    calendar.setDisabled(true);
    return;
  }

  calendar.render();
  await loadAvailability(calendar.visibleMonth);
}

function fatal(message: string): void {
  $('#fatal-error').textContent = message;
  $('#boat-name').textContent = 'Book a houseboat';
  show('error');
}

// ---- Dates -------------------------------------------------------------------------------------------------------

function dayState(day: IsoDate): DayState {
  if (!state.loadedMonths.has(firstOfMonth(day))) {
    return { status: 'not-in-mode', stay: null, blockedName: null, insideBlockedPeriod: false, clickable: false };
  }
  return evaluateDay(state.periodType, day, state.availability, state.window);
}

/** Fetches availability for a visible month, padded so periods crossing the month edges are evaluated correctly. */
async function loadAvailability(month: IsoDate, force = false): Promise<void> {
  const key = firstOfMonth(month);
  if (state.loadedMonths.has(key) && !force) return;
  $('#calendar').setAttribute('aria-busy', 'true');
  try {
    const result = await publicApi.availability(state.slug, addDays(key, -7), addDays(addMonths(key, 1), 14));
    const unavailable = new Map(state.availability.unavailable.map((r) => [`${r.start}|${r.end}`, r]));
    for (const r of result.unavailable) unavailable.set(`${r.start}|${r.end}`, r);
    const enquire = new Map(state.availability.enquireOnly.map((r) => [`${r.firstNight}|${r.name}`, r]));
    for (const r of result.enquireOnly) enquire.set(`${r.firstNight}|${r.name}`, r);
    state.availability = { unavailable: [...unavailable.values()], enquireOnly: [...enquire.values()] };
    state.loadedMonths.add(key);
  } catch {
    pageNotice('We could not load availability. Please try again.');
  } finally {
    $('#calendar').removeAttribute('aria-busy');
    calendar.render();
  }
}

async function reloadAvailability(): Promise<void> {
  state.availability = { unavailable: [], enquireOnly: [] };
  state.loadedMonths.clear();
  await loadAvailability(calendar.visibleMonth, true);
}

/** "Check-in 11am – 2pm · Check-out by 8am (AEDT)" for the selected dates, or the visible month. */
function updateTimes(): void {
  const s = state.boat!.settings;
  const from = state.selection?.start ?? calendar?.visibleMonth ?? s.today;
  $('#times').textContent = checkInOutText(s, from, state.selection?.end ?? from);
}

function setUpDates(): void {
  for (const radio of $$<HTMLInputElement>('#period-options input')) {
    radio.addEventListener('change', () => {
      state.periodType = radio.value as OnlinePeriodType;
      setSelection(null);
    });
  }
  const section = $('section[data-step="dates"]');
  $('[data-next]', section).addEventListener('click', () => {
    if (state.selection) {
      pageNotice(null);
      show('details');
    }
  });
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
  const settings = state.boat!.settings;
  $('#selection-summary').textContent = stay
    ? `Selected: check-in ${formatLongDate(stay.start)} (${settings.checkInTime} – ${settings.checkInWindowEnd}), ` +
      `check-out ${formatLongDate(stay.end)} by ${settings.checkOutTime}.`
    : '';
  ($('section[data-step="dates"] [data-next]') as HTMLButtonElement).disabled = !stay;
  updateTimes();
  calendar.render();
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
    document.querySelector<HTMLElement>(`[data-field="${errors[0]![0]}"] input:not(.phone__country)`)?.focus();
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
      await saveDraft('Guests');
    }
    if (!state.draftId) {
      // No draft yet, or the old one was completed or deleted.
      if (!validateDetails(true)) return;
      const d = details();
      const created = await publicApi.createDraft({
        slug: state.slug,
        periodType: state.periodType,
        startDate: state.selection!.start,
        fullName: d.fullName,
        email: d.email,
        mobile: d.mobile!,
        turnstileToken: turnstile.value,
      });
      rememberDraft(created.draftId);
      turnstile.reset();
      void saveDraft('Guests');
    }
    pageNotice(null);
    show('guests');
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

// ---- Guests ------------------------------------------------------------------------------------------------------

function setUpGuests(): void {
  const boat = state.boat!;
  const adultsSelect = $<HTMLSelectElement>('#adults');
  const childrenSelect = $<HTMLSelectElement>('#children');
  for (const n of adultOptions(boat.maxNoOfGuests)) adultsSelect.append(h('option', { value: n }, String(n)));
  renderChildOptions();
  $('#bedding-hint').textContent = boat.adultBeds !== null
    ? `${boat.name} takes up to ${boat.maxNoOfGuests} guests and has ${boat.adultBeds} adult beds and ${boat.kidsBeds ?? 0} kids beds` +
      `${boat.beddingDescription ? `: ${boat.beddingDescription}` : '.'}`
    : `${boat.name} takes up to ${boat.maxNoOfGuests} guests.`;

  const onChange = async (select: HTMLSelectElement) => {
    state.adults = Number(adultsSelect.value) || null;
    if (select === adultsSelect) renderChildOptions();
    state.children = Number(childrenSelect.value) || 0;
    state.roomingText = null;
    clearFieldError('numberOfAdults');
    clearFieldError('numberOfChildren');
    if (state.adults && needsRoomingWarning(state.adults, state.children, boat.adultBeds, boat.kidsBeds)) {
      if (!(await confirmRooming(state.adults, state.children))) {
        select.value = select === adultsSelect ? '' : '0';
        if (select === adultsSelect) state.adults = null;
        else state.children = 0;
        renderChildOptions();
        select.focus();
      }
    }
    updateGuestsNext();
  };
  adultsSelect.addEventListener('change', () => void onChange(adultsSelect));
  childrenSelect.addEventListener('change', () => void onChange(childrenSelect));

  const section = $('section[data-step="guests"]');
  $('[data-back]', section).addEventListener('click', () => show('details'));
  $('[data-next]', section).addEventListener('click', () => {
    void saveDraft('Addons');
    show('addons');
  });
}

/** Children: 0 … max guests − adults; changing adults clamps the children already chosen. */
function renderChildOptions(): void {
  const max = state.boat!.maxNoOfGuests;
  const select = $<HTMLSelectElement>('#children');
  const adults = state.adults ?? 1;
  state.children = clampChildren(max, adults, state.children);
  clear(select);
  for (const n of childOptions(max, adults)) select.append(h('option', { value: n, selected: n === state.children }, String(n)));
}

/** Shows the rooming warning (text generated by the server) and records the exact text accepted. */
async function confirmRooming(adults: number, children: number): Promise<boolean> {
  let text: string | null;
  try {
    const quote = await publicApi.quote(state.slug, {
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfAdults: adults,
      numberOfChildren: children,
      addons: [],
    });
    text = quote.roomingWarningText;
  } catch (e) {
    pageNotice(e instanceof ApiError ? e.messages.join(' ') : 'Something went wrong. Please try again.');
    return false;
  }
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
  if (choice === 'accept') state.roomingText = text;
  return choice === 'accept';
}

function updateGuestsNext(): void {
  const boat = state.boat!;
  const needsRooming = !!state.adults && needsRoomingWarning(state.adults, state.children, boat.adultBeds, boat.kidsBeds);
  const ok = !!state.adults && (!needsRooming || !!state.roomingText);
  ($('section[data-step="guests"] [data-next]') as HTMLButtonElement).disabled = !ok;
}

// ---- Add-ons -----------------------------------------------------------------------------------------------------

function setUpAddons(): void {
  const section = $('section[data-step="addons"]');
  $('[data-back]', section).addEventListener('click', () => show('guests'));
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

/** Remaining capacity of the capacity-limited add-ons for the selected stay (cached per stay). */
async function loadRemaining(): Promise<void> {
  const stay = state.selection!;
  const key = `${stay.start}|${stay.end}`;
  if (state.remainingFor === key) return;
  state.remaining.clear();
  await Promise.all(state.boat!.addons.filter((a) => a.hasCapacity).map(async (a) => {
    try {
      state.remaining.set(a.id, (await publicApi.addonAvailability(a.id, stay.start, stay.end)).remaining);
    } catch {
      // shown without a limit; the server still enforces it
    }
  }));
  state.remainingFor = key;
}

async function renderAddons(): Promise<void> {
  const list = $('#addon-list');
  const addons = state.boat!.addons;
  if (addons.some((a) => a.hasCapacity)) {
    list.setAttribute('aria-busy', 'true');
    await loadRemaining();
    list.setAttribute('aria-busy', 'false');
  }
  clear(list);
  const shown = addons.map((addon) => ({ addon, view: capacityView(addon, state.remaining.get(addon.id)) }))
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
    state.quote = await publicApi.quote(state.slug, {
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfAdults: state.adults!,
      numberOfChildren: state.children,
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
  const guests = `${quote.numberOfAdults} adult${quote.numberOfAdults === 1 ? '' : 's'}, ` +
    `${quote.numberOfChildren} child${quote.numberOfChildren === 1 ? '' : 'ren'} (under 12)`;

  container.append(
    h('dl', { class: 'summary' },
      row('Boat', quote.boatName),
      row('Check-in', `${formatLongDate(quote.startDate)}, ${quote.checkInTime} – ${quote.checkInWindowEnd} ${quote.timeZoneLabel}`),
      row('Check-out', `${formatLongDate(quote.endDate)} by ${quote.checkOutTime}`),
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
  try {
    state.booking = await publicApi.createBooking({
      draftId: state.draftId,
      slug: state.slug,
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfAdults: state.adults!,
      numberOfChildren: state.children,
      fullName: d.fullName,
      email: d.email,
      mobile: d.mobile ?? null,
      addons: addonRequests(),
      roomingWarningAccepted: !!state.roomingText,
      roomingWarningText: state.roomingText,
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
    setSelection(null);
    void reloadAvailability();
    show('dates');
    pageNotice('Sorry, those dates were just taken. Please choose other dates.');
    return;
  }
  const fields = Object.keys(e.fieldErrors);
  if (fields.includes('draftId')) rememberDraft(null);
  if (fields.includes('addons')) state.remainingFor = null; // show refreshed remaining figures
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
