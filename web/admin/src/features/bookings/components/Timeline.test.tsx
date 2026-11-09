import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api, boats, emptyTimeline } from '@/testing/server';
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
    renderAdmin(<Timeline range={rangeFor('month', '2026-10-01')} data={data} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
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
    renderAdmin(<Timeline range={rangeFor('week', '2026-10-19')} data={data} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    const a = screen.getByTestId('bar-a');
    expect(parseFloat(a.style.left)).toBeCloseTo((100 / 7) * (11 / 24));
    expect(a.dataset.lane).toBe('0');
    expect(screen.getByTestId('bar-b').dataset.lane).toBe('1');
    expect(screen.getByTestId('bar-c').dataset.lane).toBe('0');
  });

  it('fills the width in month view and only scrolls below the minimum column width', () => {
    const { unmount } = renderAdmin(<Timeline range={rangeFor('month', '2026-10-01')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    const grid = screen.getByTestId('timeline-grid');
    expect(grid.style.minWidth).toBe('calc(170px + 868px)'); // 31 days × 28px
    expect(screen.getAllByRole('columnheader')).toHaveLength(32);
    unmount();
    renderAdmin(<Timeline range={rangeFor('week', '2026-10-19')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={noop} />);
    expect(screen.getByTestId('timeline-grid').style.minWidth).toBe('');
  });

  it('opens a new booking with the boat and date of an empty cell', async () => {
    const opened: [string, string][] = [];
    renderAdmin(<Timeline range={rangeFor('week', '2026-10-19')} data={emptyTimeline} todayIso="2026-10-01" onOpenBooking={noop} onNewBooking={(b, d) => opened.push([b, d])} />);
    await userEvent.setup().click(screen.getByRole('button', { name: 'New booking for Misty Blue on 21/10/2026' }));
    expect(opened).toEqual([['b-mb', '2026-10-21']]);
  });
});

describe('Bookings page', () => {
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
