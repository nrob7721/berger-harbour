import { addDays, addMonths, dayOfWeek, daysInMonth, firstOfMonth, formatDate, monthTitle, sydneyToday, weekdayShort, type IsoDate } from '@shared/dates';
import { clear, h } from '@shared/dom';
import { adminApi, type Timeline, type TimelineBooking } from '../api';
import { assignLanes, bookingSpan, nightsSpan } from '../timeline';
import { loading, PAYMENT_STATUS_LABEL, showError, STATUS_LABEL } from '../ui';
import { openBookingDialog } from './booking-dialog';

const COL = 40; // px per day column
const LANE = 30; // px per bar lane

const view = {
  month: firstOfMonth(sydneyToday()),
  includeInactive: false,
};

export async function renderBookings(page: HTMLElement): Promise<void> {
  const grid = h('div', { class: 'timeline-wrapper' });
  const title = h('h1', { class: 'toolbar__title', 'aria-live': 'polite' });
  const inactiveToggle = h('input', { type: 'checkbox', id: 'show-inactive', checked: view.includeInactive });
  const searchInput = h('input', { type: 'search', placeholder: 'Search name, email or reference', 'aria-label': 'Search bookings', class: 'search__input' });
  const results = h('div', { class: 'search__results', role: 'region', 'aria-label': 'Search results', 'aria-live': 'polite' });

  const reload = async () => {
    title.textContent = monthTitle(view.month);
    loading(grid);
    try {
      const timeline = await adminApi.timeline(view.month, addMonths(view.month, 1), view.includeInactive);
      clear(grid);
      grid.append(renderTimeline(timeline, reload));
    } catch (e) {
      clear(grid);
      showError(grid, e);
    }
  };

  const go = (month: IsoDate) => {
    view.month = month;
    void reload();
  };

  inactiveToggle.addEventListener('change', () => {
    view.includeInactive = inactiveToggle.checked;
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
        h('button', { type: 'button', class: 'button', 'aria-label': 'Previous month', onclick: () => go(addMonths(view.month, -1)) }, '‹ Prev'),
        h('button', { type: 'button', class: 'button', onclick: () => go(firstOfMonth(sydneyToday())) }, 'Today'),
        h('button', { type: 'button', class: 'button', 'aria-label': 'Next month', onclick: () => go(addMonths(view.month, 1)) }, 'Next ›'),
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

function renderTimeline(t: Timeline, reload: () => Promise<void>): HTMLElement {
  const days = daysInMonth(t.from);
  const width = days * COL;
  const today = sydneyToday();
  const dayList: IsoDate[] = Array.from({ length: days }, (_, i) => addDays(t.from, i));
  const isWeekend = (d: IsoDate) => dayOfWeek(d) === 0 || dayOfWeek(d) === 6;

  const header = h('div', { class: 'tl-row tl-row--header' },
    h('div', { class: 'tl-name' }, 'Boat'),
    h('div', { class: 'tl-track', style: `width:${width}px` },
      ...dayList.map((d) => h('div', {
        class: `tl-day-head${isWeekend(d) ? ' tl-weekend' : ''}${d === today ? ' tl-today' : ''}`,
        style: `width:${COL}px`,
      }, h('span', {}, weekdayShort(dayOfWeek(d)).slice(0, 2)), h('strong', {}, String(Number(d.slice(8))))))));

  const bandTrack = h('div', { class: 'tl-track tl-track--band', style: `width:${width}px` });
  for (const bp of t.blockedPeriods) {
    const span = nightsSpan(t.from, days, bp.firstNight, bp.lastNight);
    if (span) {
      bandTrack.append(h('div', {
        class: 'tl-band',
        style: `left:${span.left * COL}px;width:${span.width * COL}px`,
        title: `${bp.name}: ${formatDate(bp.firstNight)} – ${formatDate(bp.lastNight)}`,
      }, bp.name));
    }
  }
  const band = h('div', { class: 'tl-row tl-row--band' }, h('div', { class: 'tl-name muted' }, 'Blocked periods'), bandTrack);

  const rows = t.boats.map((boat) => {
    const bookings = t.bookings.filter((b) => b.boatId === boat.id);
    const laned = assignLanes(bookings.map((b) => ({ ...b, start: b.startDate, end: b.endDate })));
    const lanes = Math.max(1, ...laned.map((l) => l.lane + 1));
    const track = h('div', { class: 'tl-track', style: `width:${width}px;height:${lanes * LANE + 8}px` });

    for (const d of dayList) {
      track.append(h('button', {
        type: 'button',
        class: `tl-cell${isWeekend(d) ? ' tl-weekend' : ''}${d === today ? ' tl-today' : ''}`,
        style: `width:${COL}px`,
        'aria-label': `New booking for ${boat.name} starting ${formatDate(d)}`,
        tabindex: -1,
        onclick: () => void openBookingDialog({ boatId: boat.id, startDate: d, onSaved: reload }),
      }));
    }
    for (const bp of t.blockedPeriods) {
      const span = nightsSpan(t.from, days, bp.firstNight, bp.lastNight);
      if (span) track.append(h('div', { class: 'tl-blocked', style: `left:${span.left * COL}px;width:${span.width * COL}px` }));
    }
    for (const u of t.unavailabilities.filter((x) => x.boatId === boat.id)) {
      const span = nightsSpan(t.from, days, u.firstNight, u.lastNight);
      if (span) {
        track.append(h('div', {
          class: 'bar bar--unavailable',
          style: `left:${span.left * COL}px;width:${span.width * COL}px;top:4px;height:${lanes * LANE - 4}px`,
          title: `Unavailable ${formatDate(u.firstNight)} – ${formatDate(u.lastNight)}${u.comments ? `: ${u.comments}` : ''}`,
        }, h('span', { class: 'bar__label' }, 'Unavailable')));
      }
    }
    for (const { item, lane } of laned) {
      const span = bookingSpan(t.from, days, item.startDate, item.endDate);
      if (span) track.append(bar(item, span.left * COL, span.width * COL, lane, reload));
    }

    return h('div', { class: `tl-row${boat.isActive ? '' : ' tl-row--inactive-boat'}` },
      h('div', { class: 'tl-name', title: boat.isActive ? boat.name : `${boat.name} (inactive)` }, boat.name),
      track);
  });

  return h('div', { class: 'timeline', role: 'region', 'aria-label': `Bookings timeline for ${monthTitle(t.from)}`, tabindex: 0 },
    header, band, ...rows);
}

function bar(b: TimelineBooking, left: number, width: number, lane: number, reload: () => Promise<void>): HTMLElement {
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
    style: `left:${left}px;width:${Math.max(width - 2, 8)}px;top:${4 + lane * LANE}px;height:${LANE - 4}px`,
    title: description,
    'aria-label': description,
    onclick: () => void openBookingDialog({ id: b.id, onSaved: reload }),
  }, h('span', { class: 'bar__label' }, label));
}
