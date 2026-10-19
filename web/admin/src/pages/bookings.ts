import { addDays, dayOfWeek, formatDate, sydneyToday, weekdayShort, type IsoDate } from '@shared/dates';
import { clear, h } from '@shared/dom';
import { adminApi, type Timeline, type TimelineBooking } from '../api';
import {
  assignLanes, blockedSpan, bookingSpan, stamp, stepView, switchView, todayView, unavailabilitySpan, viewRange, viewTitle,
  type Span, type TimelineMode, type TimelineView,
} from '../timeline';
import { loading, PAYMENT_STATUS_LABEL, showError, STATUS_LABEL } from '../ui';
import { openBookingDialog } from './booking-dialog';

const COL = 40; // px per day column in Month view; Week view stretches its 7 columns to the width
const LANE = 30; // px per bar lane
const MODE_KEY = 'berger-harbour.admin.timeline-mode';

function recallMode(): TimelineMode {
  try {
    return localStorage.getItem(MODE_KEY) === 'Week' ? 'Week' : 'Month';
  } catch {
    return 'Month';
  }
}

function rememberMode(mode: TimelineMode): void {
  try {
    localStorage.setItem(MODE_KEY, mode);
  } catch {
    // storage may be unavailable (private mode, blocked site data); the view then resets to Month next time
  }
}

const state: { view: TimelineView; includeInactive: boolean } = {
  view: todayView(recallMode(), sydneyToday()),
  includeInactive: false,
};

export async function renderBookings(page: HTMLElement): Promise<void> {
  const grid = h('div', { class: 'timeline-wrapper' });
  const title = h('h1', { class: 'toolbar__title', 'aria-live': 'polite' });
  const inactiveToggle = h('input', { type: 'checkbox', id: 'show-inactive', checked: state.includeInactive });
  const searchInput = h('input', { type: 'search', placeholder: 'Search name, email or reference', 'aria-label': 'Search bookings', class: 'search__input' });
  const results = h('div', { class: 'search__results', role: 'region', 'aria-label': 'Search results', 'aria-live': 'polite' });

  const modeButtons = (['Week', 'Month'] as const).map((mode) =>
    h('button', { type: 'button', class: 'segmented__option', 'data-mode': mode, onclick: () => go(switchView(state.view, mode, sydneyToday())) }, mode));
  const unit = () => state.view.mode.toLowerCase();
  const prev = h('button', { type: 'button', class: 'button', onclick: () => go(stepView(state.view, -1)) }, '‹ Prev');
  const next = h('button', { type: 'button', class: 'button', onclick: () => go(stepView(state.view, 1)) }, 'Next ›');

  const reload = async () => {
    const view = state.view;
    title.textContent = viewTitle(view);
    prev.setAttribute('aria-label', `Previous ${unit()}`);
    next.setAttribute('aria-label', `Next ${unit()}`);
    for (const b of modeButtons) b.setAttribute('aria-pressed', String(b.dataset.mode === view.mode));
    loading(grid);
    try {
      const range = viewRange(view);
      // From the day before, so bookings and unavailabilities that end on the first day still show their last morning.
      const timeline = await adminApi.timeline(addDays(range.from, -1), range.to, state.includeInactive);
      if (view !== state.view) return; // a newer view was chosen while loading
      clear(grid);
      grid.append(renderTimeline(timeline, view, reload));
    } catch (e) {
      clear(grid);
      showError(grid, e);
    }
  };

  const go = (view: TimelineView) => {
    if (view.mode !== state.view.mode) rememberMode(view.mode);
    state.view = view;
    void reload();
  };

  inactiveToggle.addEventListener('change', () => {
    state.includeInactive = inactiveToggle.checked;
    void reload();
  });

  const search = h('form', { class: 'search', role: 'search' }, searchInput, h('button', { type: 'submit', class: 'button' }, 'Search'));
  search.addEventListener('submit', async (e) => {
    e.preventDefault();
    clear(results);
    const q = searchInput.value.trim();
    if (q.length < 2) return;
    try {
      const found = await adminApi.search(q);
      if (found.length === 0) {
        results.append(h('p', { class: 'muted' }, 'No bookings found.'));
        return;
      }
      results.append(
        h('ul', { class: 'search__list' }, ...found.map((r) =>
          h('li', {}, h('button', { type: 'button', class: 'search__item', onclick: () => void openBookingDialog({ id: r.id, onSaved: reload }) },
            h('strong', {}, r.reference),
            ` ${r.customerName} (${r.customerEmail}) — ${r.boatName}, ${formatDate(r.startDate)} → ${formatDate(r.endDate)} · ${STATUS_LABEL[r.status] ?? r.status}${r.isStandby ? ' · stand-by' : ''}`)))),
        h('button', { type: 'button', class: 'button button--small', onclick: () => clear(results) }, 'Clear results'));
    } catch (err) {
      showError(results, err);
    }
  });

  page.append(
    h('div', { class: 'toolbar' },
      h('div', { class: 'toolbar__group' },
        prev,
        h('button', { type: 'button', class: 'button', onclick: () => go(todayView(state.view.mode, sydneyToday())) }, 'Today'),
        next,
        h('div', { class: 'segmented', role: 'group', 'aria-label': 'View' }, ...modeButtons),
        title),
      h('div', { class: 'toolbar__group' },
        h('label', { class: 'toggle', for: 'show-inactive' }, inactiveToggle, ' Show inactive'),
        search,
        h('button', { type: 'button', class: 'button button--primary', onclick: () => void openBookingDialog({ onSaved: reload }) }, '+ New')),
    ),
    results,
    legend(),
    grid,
  );
  await reload();
}

function legend(): HTMLElement {
  const item = (cls: string, label: string) => h('li', {}, h('span', { class: `bar-swatch ${cls}` }), label);
  return h('ul', { class: 'timeline-legend', 'aria-label': 'Legend' },
    item('bar--Outstanding', 'Outstanding'),
    item('bar--DepositPaid', 'Deposit paid'),
    item('bar--PartPaid', 'Part paid'),
    item('bar--FullyPaid', 'Fully paid'),
    item('bar--pending', 'Pending payment'),
    item('bar--standby', 'Stand-by'),
    item('bar--unavailable', 'Unavailable'),
    item('tl-band-swatch', 'Blocked period'));
}

function renderTimeline(t: Timeline, view: TimelineView, reload: () => Promise<void>): HTMLElement {
  const { from, days } = viewRange(view);
  const week = view.mode === 'Week';
  // Positions are percentages of the track, so Week view can stretch to the width while Month view scrolls.
  const trackWidth = week ? '' : `width:${days * COL}px;`;
  const pct = (columns: number) => `${(columns / days) * 100}%`;
  const place = (span: Span) => `left:${pct(span.left)};width:${pct(span.width)}`;
  const cellWidth = `width:${pct(1)}`;
  const today = sydneyToday();
  const dayList: IsoDate[] = Array.from({ length: days }, (_, i) => addDays(from, i));
  const isWeekend = (d: IsoDate) => dayOfWeek(d) === 0 || dayOfWeek(d) === 6;

  const header = h('div', { class: 'tl-row tl-row--header' },
    h('div', { class: 'tl-name' }, 'Boat'),
    h('div', { class: 'tl-track', style: trackWidth },
      ...dayList.map((d) => h('div', {
        class: `tl-day-head${isWeekend(d) ? ' tl-weekend' : ''}${d === today ? ' tl-today' : ''}`,
        style: cellWidth,
      }, ...(week
        ? [h('strong', {}, `${weekdayShort(dayOfWeek(d))} ${formatDate(d).slice(0, 5)}`)]
        : [h('span', {}, weekdayShort(dayOfWeek(d)).slice(0, 2)), h('strong', {}, String(Number(d.slice(8))))])))));

  const bandTrack = h('div', { class: 'tl-track tl-track--band', style: trackWidth });
  for (const bp of t.blockedPeriods) {
    const span = blockedSpan(from, days, bp.firstNight, bp.lastNight);
    if (span) {
      bandTrack.append(h('div', {
        class: 'tl-band',
        style: place(span),
        title: `${bp.name}: ${formatDate(bp.firstNight)} – ${formatDate(bp.lastNight)}`,
      }, bp.name));
    }
  }
  const band = h('div', { class: 'tl-row tl-row--band' }, h('div', { class: 'tl-name muted' }, 'Blocked periods'), bandTrack);

  const rows = t.boats.map((boat) => {
    const bookings = t.bookings
      .filter((b) => b.boatId === boat.id)
      .map((b) => ({ booking: b, span: bookingSpan(from, days, b.startDate, b.endDate, b.checkInTime, b.checkOutTime) }))
      .filter((x): x is { booking: TimelineBooking; span: Span } => x.span !== null);
    const laned = assignLanes(bookings.map((x) => ({
      ...x, start: stamp(x.booking.startDate, x.booking.checkInTime), end: stamp(x.booking.endDate, x.booking.checkOutTime),
    })));
    const lanes = Math.max(1, ...laned.map((l) => l.lane + 1));
    const track = h('div', { class: 'tl-track', style: `${trackWidth}height:${lanes * LANE + 8}px` });

    for (const d of dayList) {
      track.append(h('button', {
        type: 'button',
        class: `tl-cell${isWeekend(d) ? ' tl-weekend' : ''}${d === today ? ' tl-today' : ''}`,
        style: cellWidth,
        'aria-label': `New booking for ${boat.name} starting ${formatDate(d)}`,
        tabindex: -1,
        onclick: () => void openBookingDialog({ boatId: boat.id, startDate: d, onSaved: reload }),
      }));
    }
    for (const bp of t.blockedPeriods) {
      const span = blockedSpan(from, days, bp.firstNight, bp.lastNight);
      if (span) track.append(h('div', { class: 'tl-blocked', style: place(span) }));
    }
    for (const u of t.unavailabilities.filter((x) => x.boatId === boat.id)) {
      const span = unavailabilitySpan(from, days, u.firstNight, u.lastNight);
      if (span) {
        track.append(h('div', {
          class: 'bar bar--unavailable',
          style: `${place(span)};top:4px;height:${lanes * LANE - 4}px`,
          title: `Unavailable ${formatDate(u.firstNight)} – ${formatDate(u.lastNight)}${u.comments ? `: ${u.comments}` : ''}`,
        }, h('span', { class: 'bar__label' }, 'Unavailable')));
      }
    }
    for (const { item, lane } of laned) track.append(bar(item.booking, place(item.span), lane, reload));

    return h('div', { class: `tl-row${boat.isActive ? '' : ' tl-row--inactive-boat'}` },
      h('div', { class: 'tl-name', title: boat.isActive ? boat.name : `${boat.name} (inactive)` }, boat.name),
      track);
  });

  return h('div', { class: `timeline${week ? ' timeline--week' : ''}`, role: 'region', 'aria-label': `Bookings timeline for ${viewTitle(view)}`, tabindex: 0 },
    header, band, ...rows);
}

function bar(b: TimelineBooking, position: string, lane: number, reload: () => Promise<void>): HTMLElement {
  const classes = ['bar', `bar--${b.paymentStatus}`];
  if (b.status === 'PendingPayment') classes.push('bar--pending');
  if (b.isStandby) classes.push('bar--standby');
  if (b.status === 'Cancelled' || b.status === 'Expired' || b.status === 'Superseded') classes.push('bar--inactive');
  const label = `${b.customerName} · ${b.reference}`;
  const description = `${label}: ${formatDate(b.startDate)} → ${formatDate(b.endDate)}, ${STATUS_LABEL[b.status] ?? b.status}, ` +
    `${PAYMENT_STATUS_LABEL[b.paymentStatus] ?? b.paymentStatus}${b.isStandby ? ', stand-by' : ''}`;
  return h('button', {
    type: 'button',
    class: classes.join(' '),
    style: `${position};top:${4 + lane * LANE}px;height:${LANE - 4}px`,
    title: description,
    'aria-label': description,
    onclick: () => void openBookingDialog({ id: b.id, onSaved: reload }),
  }, h('span', { class: 'bar__label' }, label));
}
