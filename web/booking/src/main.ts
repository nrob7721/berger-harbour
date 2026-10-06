import { addDays, addMonths, firstOfMonth, formatLongDate, type IsoDate } from '@shared/dates';
import { $, $$, clear, h, openDialog } from '@shared/dom';
import { formatMoney } from '@shared/format';
import { ApiError } from '@shared/http';
import { evaluateDay, type AvailabilityData, type BookingWindow, type DayState, type OnlinePeriodType, type Stay } from '@shared/periods';
import { publicApi, type CreateBookingResult, type PublicBoat, type Quote } from './api';
import { Calendar } from './calendar';
import { isEmbedded, requestClose } from './embed';
import { mountCheckout } from './stripe';
import { TurnstileWidget } from './turnstile';

type Step = 'loading' | 'dates' | 'guests' | 'addons' | 'details' | 'summary' | 'payment' | 'complete' | 'error';
const FLOW: Step[] = ['dates', 'guests', 'addons', 'details', 'summary', 'payment'];

/** Which step owns each API validation field, so errors send the customer back to the right place. */
const FIELD_STEP: Record<string, Step> = {
  periodType: 'dates',
  startDate: 'dates',
  numberOfGuests: 'guests',
  roomingWarningAccepted: 'guests',
  groupRestrictionApplies: 'guests',
  addons: 'addons',
  fullName: 'details',
  email: 'details',
  mobile: 'details',
  termsAccepted: 'details',
  turnstileToken: 'details',
};

const state = {
  slug: '',
  boat: null as PublicBoat | null,
  periodType: 'Midweek' as OnlinePeriodType,
  selection: null as Stay | null,
  availability: { unavailable: [], enquireOnly: [] } as AvailabilityData,
  loadedMonths: new Set<string>(),
  window: { earliestStart: '9999-12-31', bookingsOpenUntil: '0000-01-01' } as BookingWindow,
  guests: null as number | null,
  roomingText: null as string | null,
  groupAnswer: null as 'yes' | 'no' | null,
  addons: new Map<string, number>(),
  quote: null as Quote | null,
  booking: null as CreateBookingResult | null,
  step: 'loading' as Step,
};

let calendar: Calendar;
const turnstile = new TurnstileWidget(() => clearFieldError('turnstileToken'));

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
  $('#check-in-time').textContent = settings.checkInTime;
  $('#check-out-time').textContent = settings.checkOutTime;
  ($('#terms-link') as HTMLAnchorElement).href = settings.hireTermsUrl;
  state.window = {
    earliestStart: addDays(settings.today, settings.minimumLeadTimeDays),
    bookingsOpenUntil: settings.bookingsOpenUntil,
  };

  calendar = new Calendar({
    container: $('#calendar'),
    minMonth: state.window.earliestStart,
    maxMonth: addDays(settings.bookingsOpenUntil, 1),
    dayState,
    selection: () => state.selection,
    onActivate,
    onMonthChange: (month) => void loadAvailability(month),
  });

  setUpDates();
  setUpGuests();
  setUpAddons();
  setUpDetails();
  setUpSummary();
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
      show('guests');
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
  const summary = $('#selection-summary');
  const settings = state.boat!.settings;
  summary.textContent = stay
    ? `Selected: check-in ${formatLongDate(stay.start)} from ${settings.checkInTime}, check-out ${formatLongDate(stay.end)} by ${settings.checkOutTime}.`
    : '';
  ($('section[data-step="dates"] [data-next]') as HTMLButtonElement).disabled = !stay;
  calendar.render();
}

// ---- Guests ------------------------------------------------------------------------------------------------------

function setUpGuests(): void {
  const boat = state.boat!;
  const select = $<HTMLSelectElement>('#guests');
  for (let n = 1; n <= boat.maxNoOfGuests; n++) select.append(h('option', { value: n }, String(n)));
  $('#bedding-hint').textContent = boat.noOfBeds
    ? `${boat.name} sleeps up to ${boat.maxNoOfGuests} in ${boat.noOfBeds} beds: ${boat.beddingDescription ?? ''}`
    : '';

  select.addEventListener('change', async () => {
    const n = Number(select.value) || null;
    state.guests = n;
    state.roomingText = null;
    if (n && boat.noOfBeds && n > boat.noOfBeds) {
      const accepted = await confirmRooming(n);
      if (!accepted) {
        select.value = '';
        state.guests = null;
        select.focus();
      }
    }
    updateGuestsNext();
  });

  for (const radio of $$<HTMLInputElement>('#group-question input')) {
    radio.addEventListener('change', () => {
      state.groupAnswer = radio.value as 'yes' | 'no';
      const message = $('#group-message');
      clear(message);
      if (state.groupAnswer === 'yes') {
        message.append(h('div', { class: 'notice notice--warning', role: 'alert' },
          `Groups of under-30s or all-male groups require prior approval — please contact us on ${contactText()}.`));
      }
      updateGuestsNext();
    });
  }

  const section = $('section[data-step="guests"]');
  $('[data-back]', section).addEventListener('click', () => show('dates'));
  $('[data-next]', section).addEventListener('click', () => show('addons'));
}

/** Shows the rooming warning (text generated by the server) and records the exact text accepted. */
async function confirmRooming(guests: number): Promise<boolean> {
  let text: string | null;
  try {
    const quote = await publicApi.quote(state.slug, {
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfGuests: guests,
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
  const needsRooming = !!state.guests && !!boat.noOfBeds && state.guests > boat.noOfBeds;
  const ok = !!state.guests && (!needsRooming || !!state.roomingText) && state.groupAnswer === 'no';
  ($('section[data-step="guests"] [data-next]') as HTMLButtonElement).disabled = !ok;
}

// ---- Add-ons -----------------------------------------------------------------------------------------------------

function setUpAddons(): void {
  const section = $('section[data-step="addons"]');
  $('[data-back]', section).addEventListener('click', () => show('guests'));
  $('[data-next]', section).addEventListener('click', () => show('details'));
  // Prices depend on the period type, so the list is rebuilt whenever the step is shown.
  new MutationObserver(() => {
    if (section.classList.contains('step--active')) renderAddons();
  }).observe(section, { attributes: true, attributeFilter: ['class'] });
}

function addonPrice(prices: { midweek: number; weekend: number; week: number } | null): number | null {
  if (!prices) return null;
  return state.periodType === 'Midweek' ? prices.midweek : state.periodType === 'Weekend' ? prices.weekend : prices.week;
}

function renderAddons(): void {
  const list = $('#addon-list');
  clear(list);
  const addons = state.boat!.addons;
  if (addons.length === 0) {
    list.append(h('li', { class: 'muted' }, 'No add-ons are available for this boat.'));
    return;
  }
  for (const addon of addons) {
    const id = `addon-${addon.id}`;
    const price = addonPrice(addon.prices);
    const checked = state.addons.has(addon.id);
    const quantity = h('input', {
      type: 'number', min: 1, max: 99, value: state.addons.get(addon.id) ?? 1, id: `${id}-qty`,
      class: 'addon__qty', 'aria-label': `Quantity of ${addon.name}`, disabled: !checked,
    });
    const checkbox = h('input', { type: 'checkbox', id, checked });
    checkbox.addEventListener('change', () => {
      if (checkbox.checked) state.addons.set(addon.id, Math.max(1, Number(quantity.value) || 1));
      else state.addons.delete(addon.id);
      quantity.disabled = !checkbox.checked;
    });
    quantity.addEventListener('change', () => {
      const n = Math.min(99, Math.max(1, Math.round(Number(quantity.value) || 1)));
      quantity.value = String(n);
      if (checkbox.checked) state.addons.set(addon.id, n);
    });
    list.append(h('li', { class: 'addon' },
      checkbox,
      h('label', { for: id, class: 'addon__label' },
        h('strong', {}, addon.name),
        addon.description ? h('span', { class: 'muted' }, addon.description) : null),
      h('span', { class: 'addon__price' },
        addon.priceOnRequest || price === null ? 'Price on request' : `${formatMoney(price)}${addon.quantityApplies ? ' each' : ''}`),
      addon.quantityApplies ? quantity : null,
    ));
  }
}

// ---- Details -----------------------------------------------------------------------------------------------------

function setUpDetails(): void {
  const section = $('section[data-step="details"]');
  new MutationObserver(() => {
    if (section.classList.contains('step--active')) {
      turnstile.render($('#turnstile')).catch(() => setFieldError('turnstileToken', 'The security check could not load. Please refresh the page.'));
    }
  }).observe(section, { attributes: true, attributeFilter: ['class'] });

  for (const input of $$<HTMLInputElement>('#details-form input')) {
    input.addEventListener('input', () => clearFieldError(input.closest<HTMLElement>('[data-field]')?.dataset.field ?? ''));
  }
  $('[data-back]', section).addEventListener('click', () => show('addons'));
  $('[data-next]', section).addEventListener('click', () => {
    if (validateDetails()) void goToSummary();
  });
}

function details() {
  return {
    fullName: $<HTMLInputElement>('#fullName').value.trim(),
    email: $<HTMLInputElement>('#email').value.trim(),
    mobile: $<HTMLInputElement>('#mobile').value.trim(),
    termsAccepted: $<HTMLInputElement>('#terms').checked,
  };
}

function validateDetails(): boolean {
  const d = details();
  const errors: [string, string][] = [];
  if (!d.fullName) errors.push(['fullName', 'Please enter your full name.']);
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(d.email)) errors.push(['email', 'Please enter a valid email address.']);
  const digits = d.mobile.replace(/\D/g, '');
  if (digits.length < 8 || digits.length > 15) errors.push(['mobile', 'Please enter a valid mobile number.']);
  if (!d.termsAccepted) errors.push(['termsAccepted', 'Please accept the Hire Terms and Procedures.']);
  if (!turnstile.value) errors.push(['turnstileToken', 'Please complete the security check.']);
  for (const field of ['fullName', 'email', 'mobile', 'termsAccepted', 'turnstileToken']) clearFieldError(field);
  for (const [field, message] of errors) setFieldError(field, message);
  if (errors.length > 0) {
    document.querySelector<HTMLElement>(`[data-field="${errors[0]![0]}"] input`)?.focus();
  }
  return errors.length === 0;
}

function setFieldError(field: string, message: string): void {
  const wrapper = document.querySelector<HTMLElement>(`[data-field="${field}"]`);
  if (!wrapper) return;
  clearFieldError(field);
  wrapper.classList.add('field--invalid');
  const id = `${field}-error`;
  wrapper.append(h('span', { class: 'field__error', id }, message));
  wrapper.querySelector('input')?.setAttribute('aria-describedby', id);
  wrapper.querySelector('input')?.setAttribute('aria-invalid', 'true');
}

function clearFieldError(field: string): void {
  const wrapper = document.querySelector<HTMLElement>(`[data-field="${field}"]`);
  if (!wrapper) return;
  wrapper.classList.remove('field--invalid');
  wrapper.querySelector('.field__error')?.remove();
  wrapper.querySelector('input')?.removeAttribute('aria-invalid');
}

// ---- Summary -----------------------------------------------------------------------------------------------------

function setUpSummary(): void {
  const section = $('section[data-step="summary"]');
  $('[data-back]', section).addEventListener('click', () => show('details'));
  $('#pay-button').addEventListener('click', () => void pay());
}

async function goToSummary(): Promise<void> {
  const button = $<HTMLButtonElement>('section[data-step="details"] [data-next]');
  button.disabled = true;
  try {
    state.quote = await publicApi.quote(state.slug, {
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfGuests: state.guests!,
      addons: [...state.addons].map(([addonId, quantity]) => ({ addonId, quantity })),
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
      `${a.name}${a.quantity > 1 ? ` × ${a.quantity}` : ''} — ${a.estimatedTotal !== null ? `est. ${formatMoney(a.estimatedTotal)}` : 'price to be confirmed'}`)));
  const schedule = h('ul', { class: 'plain-list' }, ...quote.milestones.map((m) =>
    h('li', {}, `${m.label}: ${formatMoney(m.amount)}${m.dueDate ? ` by ${formatLongDate(m.dueDate)}` : ''}`)));

  container.append(
    h('dl', { class: 'summary' },
      row('Boat', quote.boatName),
      row('Check-in', `${formatLongDate(quote.startDate)} from ${quote.checkInTime}`),
      row('Check-out', `${formatLongDate(quote.endDate)} by ${quote.checkOutTime}`),
      row('Guests', String(quote.numberOfGuests)),
      row('Season', quote.seasonName),
      row('Hire price', formatMoney(quote.hirePrice)),
      row('Requested add-ons', addons),
      row('Payment schedule', schedule),
    ),
    h('p', { class: 'muted', hidden: quote.addons.length === 0 },
      'Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance.'),
    h('p', { class: 'summary__due' }, 'Amount payable now: ', h('strong', {}, formatMoney(quote.amountDueAtCheckout)),
      quote.fullPaymentAtCheckout ? ' (full payment, as the balance is already due)' : ''),
    h('p', { class: 'notice' }, `A ${formatMoney(quote.securityBond)} security bond applies — details will be provided.`),
  );
  $('#pay-button').textContent = `Pay ${formatMoney(quote.amountDueAtCheckout)}`;
}

// ---- Payment -----------------------------------------------------------------------------------------------------

async function pay(): Promise<void> {
  const button = $<HTMLButtonElement>('#pay-button');
  if (!turnstile.value) {
    show('details');
    setFieldError('turnstileToken', 'Please complete the security check again.');
    return;
  }
  button.disabled = true;
  const d = details();
  try {
    state.booking = await publicApi.createBooking({
      slug: state.slug,
      periodType: state.periodType,
      startDate: state.selection!.start,
      numberOfGuests: state.guests!,
      fullName: d.fullName,
      email: d.email,
      mobile: d.mobile,
      addons: [...state.addons].map(([addonId, quantity]) => ({ addonId, quantity })),
      roomingWarningAccepted: !!state.roomingText,
      roomingWarningText: state.roomingText,
      groupRestrictionApplies: false,
      termsAccepted: d.termsAccepted,
      turnstileToken: turnstile.value,
    });
  } catch (e) {
    handleBookingError(e);
    return;
  } finally {
    turnstile.reset();
    button.disabled = false;
  }

  $('#payment-hold').textContent =
    `Booking ${state.booking.reference}: your dates are held while you pay. Please complete payment within 30 minutes.`;
  show('payment');
  try {
    await mountCheckout($('#checkout'), state.booking.clientSecret);
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
