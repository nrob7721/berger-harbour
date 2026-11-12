import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, delay } from 'msw';
import { api, boats, emptyTimeline, server } from '@/testing/server';
import { renderAdmin } from '@/testing/render';
import { rangeFor } from '../utils/timeline';
import type { TimelineBooking } from '@/lib/types';
import { BookingsPage } from './BookingsPage';
import { Timeline } from './Timeline';

const booking = (overrides: Partial<TimelineBooking>): TimelineBooking => ({
  id: 'x', boatId: boats[0].id, reference: 'SMITH-2610120', customerName: 'Jane Smith', status: 'Active', isStandby: false,
  paymentStatus: 'DepositPaid', periodType: 'Midweek', startDate: '2026-10-12', endDate: '2026-10-16', checkInTime: '11:00', checkOutTime: '08:00',
  ...overrides,
});

const noop = () => {};

describe('Timeline', () => {
  it('labels holds "Pending payment" and active bookings with their reference', () => {
    const data = { ...emptyTimeline, bookings: [booking({ id: 'a' }), booking({ id: 'h', reference: null, status: 'PendingPayment', customerName: 'Ann Lee', startDate: '2026-10-19', endDate: '2026-10-23' })] };
    renderAdmin(<Timeline boats={boats} range={rangeFor('month', '2026-10-01')} data={data} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByTestId('bar-a')).toHaveTextContent('Jane Smith · SMITH-2610120');
    expect(screen.getByTestId('bar-h')).toHaveTextContent('Ann Lee · Pending payment');
    expect(screen.getByTestId('bar-h')).toHaveClass('pending');
    expect(screen.getByTestId('bar-a')).toHaveClass('depositPaid');
  });

  it('positions bars by time of day and stacks clashing custom times into lanes', () => {
    const data = { ...emptyTimeline, bookings: [
      booking({ id: 'a', startDate: '2026-10-19', endDate: '2026-10-21', checkOutTime: '23:00' }),
      booking({ id: 'b', startDate: '2026-10-21', endDate: '2026-10-23' }),
      booking({ id: 'c', startDate: '2026-10-23', endDate: '2026-10-25' }),
    ] };
    renderAdmin(<Timeline boats={boats} range={rangeFor('week', '2026-10-19')} data={data} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    const a = screen.getByTestId('bar-a');
    expect(parseFloat(a.style.left)).toBeCloseTo((100 / 7) * (11 / 24));
    expect(a.dataset.lane).toBe('0');
    expect(screen.getByTestId('bar-b').dataset.lane).toBe('1');
    expect(screen.getByTestId('bar-c').dataset.lane).toBe('0');
  });

  it('fills the width in month view and only scrolls below the minimum column width', () => {
    const { unmount } = renderAdmin(<Timeline boats={boats} range={rangeFor('month', '2026-10-01')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    const grid = screen.getByTestId('timeline-grid');
    expect(grid.style.minWidth).toBe('calc(170px + 868px)'); // 31 days × 28px
    expect(screen.getAllByRole('columnheader')).toHaveLength(32);
    unmount();
    renderAdmin(<Timeline boats={boats} range={rangeFor('week', '2026-10-19')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByTestId('timeline-grid').style.minWidth).toBe('');
  });

  it('opens a new booking with the boat and date of an empty cell', async () => {
    const opened: [string, string][] = [];
    renderAdmin(<Timeline boats={boats} range={rangeFor('week', '2026-10-19')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={(b, d) => opened.push([b, d])} />);
    await userEvent.setup().click(screen.getByRole('button', { name: 'New booking for Misty Blue on 21/10/2026' }));
    expect(opened).toEqual([['b-mb', '2026-10-21']]);
  });

  it('keeps inactive boats with muted names: their empty cells are not clickable but their bars still open', async () => {
    const opened: string[] = [];
    const newBookings: string[] = [];
    const data = { ...emptyTimeline, bookings: [booking({ id: 'old', boatId: 'b-mb', startDate: '2026-10-19', endDate: '2026-10-23' })] };
    const rows = boats.map((b) => (b.slug === 'misty-blue' ? { ...b, isActive: false } : b));
    renderAdmin(<Timeline boats={rows} range={rangeFor('week', '2026-10-19')} data={data} todayIso="2026-10-01" onOpenBooking={(id) => opened.push(id)} onNewBooking={(b) => newBookings.push(b)} />);
    const row = screen.getByTestId('row-misty-blue');
    expect(within(row).getByRole('rowheader')).toHaveClass('muted');
    expect(screen.queryByRole('button', { name: /New booking for Misty Blue/ })).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('cell-misty-blue-2026-10-24'));
    expect(newBookings).toEqual([]);
    await user.click(screen.getByTestId('bar-old'));
    expect(opened).toEqual(['old']);
    expect(screen.getAllByRole('button', { name: /New booking for Pacific Blue/ })).toHaveLength(7);
  });
});

describe('Timeline loading', () => {
  it('shows a large spinner in the cell area while a range loads, keeping the day headers and boat names', () => {
    renderAdmin(<Timeline boats={boats} range={rangeFor('week', '2026-10-19')} data={undefined} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByRole('status', { name: 'Loading bookings' })).toBeInTheDocument();
    expect(screen.getAllByRole('columnheader')).toHaveLength(8);
    expect(screen.getByRole('rowheader', { name: 'Pacific Blue' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /New booking for/ })).not.toBeInTheDocument();
  });

  it('on the very first load the boat-name column waits too', () => {
    renderAdmin(<Timeline boats={undefined} range={rangeFor('week', '2026-10-19')} data={undefined} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByRole('status', { name: 'Loading boats' })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Loading bookings' })).toBeInTheDocument();
  });

  it('shows the error with Retry when loading fails', async () => {
    const onRetry = vi.fn();
    renderAdmin(<Timeline boats={boats} range={rangeFor('week', '2026-10-19')} data={undefined} error={new Error('Network down')} onRetry={onRetry}
      todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Network down');
    expect(screen.queryByRole('status', { name: 'Loading bookings' })).not.toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Retry' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('never lets a slower, older response replace a newer range, and shows a loaded range without a spinner', async () => {
    const bar = (id: string, start: string, end: string) => booking({ id, startDate: start, endDate: end });
    server.use(http.get('*/api/bookings', async ({ request }) => {
      const from = new URL(request.url).searchParams.get('from');
      if (from === '2026-10-19') {
        await delay(150);
        return HttpResponse.json({ ...emptyTimeline, bookings: [bar('old', '2026-10-20', '2026-10-22')] });
      }
      return HttpResponse.json({ ...emptyTimeline, bookings: [bar('new', '2026-10-27', '2026-10-29')] });
    }));
    window.localStorage.setItem('bh-admin-timeline-view', 'week');
    renderAdmin(<BookingsPage todayIso="2026-10-21" />);
    const user = userEvent.setup();
    expect(await screen.findByRole('status', { name: 'Loading bookings' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next ›' })); // the controls stay usable while loading
    expect(await screen.findByTestId('bar-new')).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 200)); // the older range's response arrives
    expect(screen.queryByTestId('bar-old')).not.toBeInTheDocument();
    expect(screen.getByTestId('bar-new')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '‹ Prev' }));
    expect(screen.getByTestId('bar-old')).toBeInTheDocument(); // already loaded: shown straight away
    expect(screen.queryByRole('status', { name: 'Loading bookings' })).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('bar-old')).toBeInTheDocument());
  });
});

describe('Bookings page', () => {
  it.each([
    ['2026-10-15', 'Midweek', '2026-10-12', '2026-10-16'], // Thu → the Mid-week of that week
    ['2026-10-18', 'Weekend', '2026-10-16', '2026-10-19'], // Sun → the Weekend containing it
  ])('clicking an empty cell on %s pre-fills the boat and %s %s → %s', async (day, periodType, start, end) => {
    api.timeline = emptyTimeline;
    renderAdmin(<BookingsPage todayIso="2026-10-21" />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Week' }));
    await user.click(screen.getByRole('button', { name: '‹ Prev' }));
    await user.click(await screen.findByRole('button', { name: `New booking for Pacific Blue on ${day.split('-').reverse().join('/')}` }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByRole('option', { name: 'Pacific Blue' }); // boats loaded
    expect(within(dialog).getByLabelText('Boat')).toHaveValue('b-pb');
    expect(within(dialog).getByLabelText('Period type')).toHaveValue(periodType);
    expect(within(dialog).getByLabelText('Start date')).toHaveValue(start);
    expect(within(dialog).getByLabelText('End date')).toHaveValue(end);
  });

  it('"+ New" uses today with no boat', async () => {
    api.timeline = emptyTimeline;
    renderAdmin(<BookingsPage todayIso="2026-10-22" />); // a Thursday
    await userEvent.setup().click(screen.getByRole('button', { name: '+ New' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText('Boat')).toHaveValue('');
    expect(within(dialog).getByLabelText('Period type')).toHaveValue('Midweek');
    expect(within(dialog).getByLabelText('Start date')).toHaveValue('2026-10-19');
    expect(within(dialog).getByLabelText('End date')).toHaveValue('2026-10-23');
  });

  it('switches between month and week, remembering the view', async () => {
    api.timeline = emptyTimeline;
    renderAdmin(<BookingsPage todayIso="2026-10-21" />);
    expect(screen.getByRole('heading', { name: 'October 2026' })).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Week' }));
    expect(screen.getByRole('heading', { name: 'Mon 19/10/2026 – Sun 25/10/2026' })).toBeInTheDocument();
    expect(window.localStorage.getItem('bh-admin-timeline-view')).toBe('week');
    await user.click(screen.getByRole('button', { name: 'Next ›' }));
    expect(screen.getByRole('heading', { name: 'Mon 26/10/2026 – Sun 01/11/2026' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Today' }));
    expect(screen.getByRole('heading', { name: 'Mon 19/10/2026 – Sun 25/10/2026' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Month' }));
    expect(screen.getByRole('heading', { name: 'October 2026' })).toBeInTheDocument();
  });
});
