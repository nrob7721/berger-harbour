import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { apiState, boats, details, settings } from '@/testing/server';
import { currentState, renderWithBooking } from '@/testing/render';
import type { BookingState } from '@/lib/booking-state';
import type { BoatDetails } from '@/lib/types';
import { DatesAndBoatStep } from './DatesAndBoatStep';

function renderStep(state: Partial<BookingState> = {}, requested: BoatDetails | null = null, onContinue = vi.fn()) {
  const r = renderWithBooking(<DatesAndBoatStep fleet={boats} settings={settings} requestedBoat={requested} onContinue={onContinue} />, state);
  return { ...r, onContinue };
}

/** The calendar opens on August 2026 (today + 30 days); October is two months on. */
async function goToOctober(user = userEvent.setup()) {
  await user.click(screen.getByRole('button', { name: 'Next month' }));
  await user.click(screen.getByRole('button', { name: 'Next month' }));
  await screen.findByRole('grid', { name: 'October 2026' });
  await waitFor(() => expect(screen.getByRole('grid')).toHaveAttribute('aria-busy', 'false'));
  return user;
}

const day = (iso: string) => screen.getByTestId(`day-${iso}`);

describe('Dates & boat: calendar', () => {
  it('selects the whole period and highlights non-clickable inner days with the selection colour', async () => {
    renderStep({ periodType: 'Week' });
    const user = await goToOctober();
    expect(day('2026-10-13')).toHaveAttribute('aria-disabled', 'true'); // Tuesday isn't clickable in Week mode
    await user.click(day('2026-10-12'));
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-19' });
    expect(day('2026-10-12')).toHaveClass('in');
    expect(day('2026-10-19')).toHaveClass('out');
    for (const inner of ['2026-10-13', '2026-10-14', '2026-10-15']) {
      expect(day(inner)).toHaveClass('selected', 'inner');
      expect(day(inner)).not.toHaveClass('disabled');
    }
    expect(screen.getByTestId('selected-line')).toHaveTextContent('Selected: check-in Mon 12/10/2026, check-out Mon 19/10/2026.');
    expect(screen.getByTestId('live-region')).toHaveTextContent('Selected: check-in Mon 12/10/2026');
  });

  it('shows adjacent-month days muted, not clickable, and with the muted selection colour', async () => {
    renderStep({ periodType: 'Week' });
    const user = await goToOctober();
    const leading = day('2026-09-28');
    expect(leading).toHaveClass('adjacent');
    expect(leading.tagName).toBe('DIV');
    await user.click(day('2026-10-26')); // Mon 26/10 → Mon 02/11
    expect(day('2026-11-01')).toHaveClass('adjacent', 'adjacentSelected', 'inner');
    expect(day('2026-10-26')).toHaveClass('in');
  });

  it('maps clicks per mode and changing the period type clears the period but keeps the boat', async () => {
    renderStep({ selectedSlug: 'pacific-blue' });
    const user = await goToOctober();
    await user.click(day('2026-10-16')); // Fri in mid-week mode: Mon 12 → Fri 16
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-16' });
    await user.click(screen.getByRole('radio', { name: /Weekend/ }));
    expect(currentState().period).toBeNull();
    expect(currentState().selectedSlug).toBe('pacific-blue');
    await user.click(day('2026-10-12')); // Monday in weekend mode: the weekend ending that day
    expect(currentState().period).toEqual({ start: '2026-10-09', end: '2026-10-12' });
  });

  it('shows the enquiry pop-up for periods overlapping a blocked period', async () => {
    apiState.enquireOnly = [{ name: 'Christmas / New Year 2026–27', firstNight: '2026-12-20', lastNight: '2027-01-05' }];
    renderStep();
    const user = userEvent.setup();
    for (let i = 0; i < 4; i++) await user.click(screen.getByRole('button', { name: 'Next month' }));
    await screen.findByRole('grid', { name: 'December 2026' });
    await waitFor(() => expect(day('2026-12-22')).toHaveClass('enquire'));
    await user.click(day('2026-12-18')); // Mon 14 → Fri 18 is fine
    expect(currentState().period).toEqual({ start: '2026-12-14', end: '2026-12-18' });
    await user.click(screen.getByRole('radio', { name: /Mon–Mon/ }));
    await user.click(day('2026-12-18')); // Fri 18 → Fri 25 overlaps Christmas
    expect(await screen.findByText('Bookings over Christmas / New Year 2026–27 must be made by phone or email: 02 4455 6677 / info@berger.test.')).toBeVisible();
    expect(currentState().period).toBeNull();
  });

  it('shows the check-in line with the zone label', async () => {
    renderStep();
    expect(screen.getByTestId('check-in-line')).toHaveTextContent('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    expect(screen.getByTestId('check-in-line')).toHaveTextContent('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
  });
});

describe('Dates & boat: calendar boats and the checkbox', () => {
  it('greys a period when no candidate is free (unticked) or the selected boat is busy (ticked)', async () => {
    apiState.unavailable = { kalinda: [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep({ selectedSlug: 'kalinda', showSelectedOnly: true }, details('kalinda'));
    const user = await goToOctober();
    expect(day('2026-10-13')).toHaveAttribute('aria-disabled', 'true');
    await user.click(screen.getByRole('checkbox', { name: "Show Kalinda's availability only" }));
    expect(day('2026-10-13')).not.toHaveAttribute('aria-disabled');
  });

  it('applies guest, Spa and Pet filters to the candidates the calendar uses', async () => {
    apiState.unavailable = { 'pacific-blue': [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep();
    const user = await goToOctober();
    expect(day('2026-10-13')).not.toHaveAttribute('aria-disabled');
    await user.click(screen.getByRole('checkbox', { name: 'Spa' })); // only Pacific Blue, which is busy
    expect(day('2026-10-13')).toHaveAttribute('aria-disabled', 'true');
  });

  it('is unticked and disabled without a selected boat, enabled when one is selected, and unchanged by selecting another card', async () => {
    renderStep();
    const box = screen.getByRole('checkbox', { name: "Show the selected boat's availability only" });
    expect(box).toBeDisabled();
    expect(box).not.toBeChecked();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda'));
    const enabled = screen.getByRole('checkbox', { name: "Show Kalinda's availability only" });
    expect(enabled).toBeEnabled();
    expect(enabled).not.toBeChecked();
    await user.click(enabled);
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(screen.getByRole('checkbox', { name: "Show Misty Blue's availability only" })).toBeChecked();
  });

  it('starts ticked for an active requested boat', () => {
    renderStep({ selectedSlug: 'kalinda', showSelectedOnly: true, requestedSlug: 'kalinda' }, details('kalinda'));
    expect(screen.getByRole('checkbox', { name: "Show Kalinda's availability only" })).toBeChecked();
    expect(screen.getByTestId('boat-kalinda')).toHaveAttribute('aria-checked', 'true');
  });

  it('deselects the boat when the period makes it ineligible, keeping the period and unticking the checkbox', async () => {
    apiState.unavailable = { kalinda: [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep({ selectedSlug: 'kalinda', showSelectedOnly: false }, details('kalinda'));
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    await waitFor(() => expect(currentState().selectedSlug).toBeNull());
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-16' });
    expect(currentState().showSelectedOnly).toBe(false);
  });

  it('deselects the boat when guests or features make it ineligible, without clearing the period', async () => {
    renderStep({ selectedSlug: 'runaway-bear', period: { start: '2026-10-12', end: '2026-10-16' } });
    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText('Adults'), '3');
    await waitFor(() => expect(currentState().selectedSlug).toBeNull());
    expect(currentState().period).not.toBeNull();
  });
});

describe('Dates & boat: filters', () => {
  it('limits children to the largest berth minus adults and clamps them when adults change', async () => {
    renderStep();
    const user = userEvent.setup();
    expect(screen.getAllByRole('option', { name: /^\d+$/ }).length).toBeGreaterThan(0);
    await user.selectOptions(screen.getByLabelText('Adults'), '10');
    const children = screen.getByLabelText('Children (Under 12)') as HTMLSelectElement;
    expect(Array.from(children.options).map((o) => o.value)).toEqual(['0', '1', '2']);
    await user.selectOptions(children, '2');
    await user.selectOptions(screen.getByLabelText('Adults'), '12');
    expect(currentState().children).toBe(0);
    expect(Array.from((screen.getByLabelText('Children (Under 12)') as HTMLSelectElement).options).map((o) => o.value)).toEqual(['0']);
  });
});

describe('Dates & boat: boat cards', () => {
  it('always lists the requested boat first, then the others under a separator', () => {
    renderStep({}, details('misty-blue'));
    const cards = screen.getAllByRole('radio').filter((c) => c.dataset.slug);
    expect(cards.map((c) => c.dataset.slug)).toEqual(['misty-blue', 'kalinda', 'pacific-blue', 'runaway-bear']);
    expect(screen.getByText('Other boats available')).toBeInTheDocument();
    expect(cards[0]).toHaveTextContent('Misty Blue9 berthPet friendly');
  });

  it('greys an inactive requested boat with its reason and selects nothing', () => {
    renderStep({}, details('old-boat', { name: 'Old Boat', isActive: false, berth: 10 }));
    const card = screen.getByTestId('boat-old-boat');
    expect(card).toHaveAttribute('aria-disabled', 'true');
    expect(within(card).getByText('Not available for online booking')).toBeInTheDocument();
  });

  it('shows "too small" and "doesn\'t match" reasons on the requested boat', async () => {
    renderStep({}, details('misty-blue'));
    const user = userEvent.setup();
    await user.click(screen.getByRole('checkbox', { name: 'Spa' }));
    expect(within(screen.getByTestId('boat-misty-blue')).getByText("Doesn't match your filters")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Adults'), '10');
    expect(within(screen.getByTestId('boat-misty-blue')).getByText('Too small for 10 guests')).toBeInTheDocument();
  });

  it('hides other boats not free for the period and explains an empty list', async () => {
    apiState.unavailable = {
      'pacific-blue': [{ start: '2026-10-12', end: '2026-10-16' }],
      'runaway-bear': [{ start: '2026-10-12', end: '2026-10-16' }],
      'misty-blue': [{ start: '2026-10-12', end: '2026-10-16' }],
    };
    renderStep({}, details('kalinda'));
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    expect(await screen.findByText('No other boats are available for these dates.')).toBeInTheDocument();
    expect(screen.queryByTestId('boat-pacific-blue')).not.toBeInTheDocument();
    expect(screen.getByTestId('boat-kalinda')).toBeInTheDocument();
  });

  it('says no boats match when no candidate at all is free', async () => {
    apiState.unavailable = { 'pacific-blue': [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep({ spa: true, period: { start: '2026-10-12', end: '2026-10-16' } });
    expect(await screen.findByText('No boats match your filters for these dates.')).toBeInTheDocument();
  });

  it('selects cards with the keyboard and announces the boat', async () => {
    renderStep();
    const user = userEvent.setup();
    screen.getByTestId('boat-kalinda').focus();
    await user.keyboard('{ArrowDown}');
    expect(screen.getByTestId('boat-pacific-blue')).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(currentState().selectedSlug).toBe('pacific-blue');
    expect(screen.getByTestId('live-region')).toHaveTextContent('Selected boat: Pacific Blue.');
  });
});

describe('Dates & boat: base hire rate and continue', () => {
  it('shows the selected boat\'s price once dates and a boat are chosen', async () => {
    renderStep();
    expect(screen.getByTestId('base-hire-rate')).toHaveTextContent('Select dates and a boat');
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    expect(screen.getByTestId('base-hire-rate')).toHaveTextContent('Select dates and a boat');
    await user.click(screen.getByTestId('boat-kalinda'));
    await waitFor(() => expect(screen.getByTestId('base-hire-rate')).toHaveTextContent('$2,535.00'));
    expect(within(screen.getByTestId('boat-kalinda')).getByText('$2,535.00')).toBeInTheDocument();
  });

  it('shows the rooming warning on Continue, and again after the boat or guests change', async () => {
    const { onContinue } = renderStep({ selectedSlug: 'kalinda', adults: 9, period: { start: '2026-10-12', end: '2026-10-16' } }); // 8 beds
    const user = userEvent.setup();
    const cont = screen.getByRole('button', { name: 'Continue' });
    await waitFor(() => expect(cont).toBeEnabled());
    await user.click(cont);
    const dialog = await screen.findByRole('dialog', { name: 'Sleeping arrangements' });
    expect(dialog).toHaveTextContent('This boat has 6 adult beds and 2 children\'s beds (4 queens and bunks). With 9 guests');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(onContinue).not.toHaveBeenCalled();
    await user.click(cont);
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Accept' }));
    expect(onContinue).toHaveBeenCalledTimes(1);
    expect(currentState().rooming?.text).toContain('With 9 guests');

    await user.click(cont); // same boat and guests: no warning
    expect(onContinue).toHaveBeenCalledTimes(2);
    await user.selectOptions(screen.getByLabelText('Children (Under 12)'), '1');
    await user.click(cont);
    expect(await screen.findByRole('dialog', { name: 'Sleeping arrangements' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Accept' }));
    await user.click(screen.getByTestId('boat-pacific-blue'));
    await waitFor(() => expect(cont).toBeEnabled());
    await user.click(cont);
    expect(await screen.findByRole('dialog', { name: 'Sleeping arrangements' })).toBeInTheDocument();
  });

  it('is disabled until a period and a boat are chosen', async () => {
    renderStep({ selectedSlug: 'kalinda' });
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });
});
