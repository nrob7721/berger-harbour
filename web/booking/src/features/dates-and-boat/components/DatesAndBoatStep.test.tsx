import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { apiState, boats, details, settings } from '@/testing/server';
import { currentState, renderWithBooking } from '@/testing/render';
import { setSmallScreen } from '@/testing/screen-size';
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
  it('selects the whole period and highlights its inner days with the selection colour', async () => {
    renderStep({ typeOfStay: 'WeekMon' });
    const user = await goToOctober();
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
    renderStep({ typeOfStay: 'WeekMon' });
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

  it('makes every day clickable in Week (Fri–Fri), selecting the week whose nights contain it', async () => {
    renderStep({ typeOfStay: 'WeekFri' });
    const user = await goToOctober();
    for (const d of ['2026-10-13', '2026-10-14', '2026-10-17', '2026-10-18']) expect(day(d)).not.toHaveAttribute('aria-disabled');
    await user.click(day('2026-10-15')); // Thursday → Fri 09/10 → Fri 16/10, starting before the clicked day
    expect(currentState().period).toEqual({ start: '2026-10-09', end: '2026-10-16' });
    await user.click(day('2026-10-16')); // a Friday starts on that Friday
    expect(currentState().period).toEqual({ start: '2026-10-16', end: '2026-10-23' });
  });

  it('makes every day clickable in Week (Mon–Mon), and switching between the two Weeks clears the period', async () => {
    renderStep({ typeOfStay: 'WeekMon', selectedSlug: 'pacific-blue' });
    const user = await goToOctober();
    await user.click(day('2026-10-17')); // Saturday → Mon 12/10 → Mon 19/10
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-19' });
    await user.click(screen.getByRole('radio', { name: 'Week (Fri–Fri)' }));
    expect(currentState().period).toBeNull();
    expect(currentState().typeOfStay).toBe('WeekFri');
    expect(currentState().selectedSlug).toBe('pacific-blue');
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
    await user.click(screen.getByRole('radio', { name: 'Week (Mon–Mon)' }));
    await user.click(day('2026-12-18')); // Fri 18 → Mon 14 → Mon 21 overlaps Christmas
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

describe('Dates & boat: calendar boats and the selected boat', () => {
  it('shows only the selected boat\'s availability, and every candidate\'s once "Click here" deselects it', async () => {
    apiState.unavailable = { kalinda: [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep({ selectedSlug: 'kalinda' }, details('kalinda'));
    const user = await goToOctober();
    expect(day('2026-10-13')).toHaveAttribute('aria-disabled', 'true');
    const message = screen.getByTestId('selected-boat-message');
    expect(message).toHaveTextContent('Currently showing availability for Kalinda. Click here to see all availability.');
    await user.click(within(message).getByRole('button', { name: 'Click here' }));
    expect(currentState().selectedSlug).toBeNull();
    expect(day('2026-10-13')).not.toHaveAttribute('aria-disabled');
    expect(screen.queryByTestId('selected-boat-message')).not.toBeInTheDocument();
  });

  it('"Click here" keeps the period', async () => {
    renderStep({ selectedSlug: 'kalinda', period: { start: '2026-10-12', end: '2026-10-16' } }, details('kalinda'));
    await userEvent.setup().click(screen.getByRole('button', { name: 'Click here' }));
    expect(currentState().selectedSlug).toBeNull();
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-16' });
  });

  it('applies guest, Spa and Pet filters to the candidates the calendar uses', async () => {
    apiState.unavailable = { 'pacific-blue': [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep();
    const user = await goToOctober();
    expect(day('2026-10-13')).not.toHaveAttribute('aria-disabled');
    await user.click(screen.getByRole('checkbox', { name: 'Spa' })); // only Pacific Blue, which is busy
    expect(day('2026-10-13')).toHaveAttribute('aria-disabled', 'true');
  });

  it('hides the selected-boat message while no boat is selected, and follows the card selected', async () => {
    renderStep();
    expect(screen.queryByTestId('selected-boat-message')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /availability only/ })).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda'));
    expect(screen.getByTestId('selected-boat-message')).toHaveTextContent('Currently showing availability for Kalinda.');
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(screen.getByTestId('selected-boat-message')).toHaveTextContent('Currently showing availability for Misty Blue.');
  });

  it('starts with an active requested boat selected, showing its availability only', () => {
    renderStep({ selectedSlug: 'kalinda', requestedSlug: 'kalinda' }, details('kalinda'));
    expect(screen.getByTestId('selected-boat-message')).toHaveTextContent('Kalinda');
    expect(screen.getByTestId('boat-kalinda')).toHaveAttribute('aria-pressed', 'true');
  });

  it('deselects the selected boat when the period makes it ineligible, keeping the period', async () => {
    apiState.unavailable = { kalinda: [{ start: '2026-10-12', end: '2026-10-16' }] };
    renderStep({ selectedSlug: 'kalinda', period: { start: '2026-10-12', end: '2026-10-16' } }, details('kalinda'));
    await waitFor(() => expect(currentState().selectedSlug).toBeNull());
    expect(currentState().period).toEqual({ start: '2026-10-12', end: '2026-10-16' });
  });

  it('deselects the boat when guests or features make it ineligible, without clearing the period', async () => {
    renderStep({ selectedSlug: 'runaway-bear', period: { start: '2026-10-12', end: '2026-10-16' } });
    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText('Adults'), '3');
    await waitFor(() => expect(currentState().selectedSlug).toBeNull());
    expect(currentState().period).not.toBeNull();
  });
});

describe('Dates & boat: layout', () => {
  it('puts the filters above the type of stay: four radios in one group, with no frame around the two Weeks', () => {
    renderStep();
    const adults = screen.getByLabelText('Adults');
    const midweek = screen.getByRole('radio', { name: 'Mid-week (Mon–Fri)' });
    expect(adults.compareDocumentPosition(midweek) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const group = screen.getByRole('group', { name: 'Type of stay' });
    expect(within(group).getAllByRole('radio').map((r) => r.getAttribute('aria-label')))
      .toEqual(['Mid-week (Mon–Fri)', 'Weekend (Fri–Mon)', 'Week (Fri–Fri)', 'Week (Mon–Mon)']);
    expect(within(group).queryByRole('group')).not.toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Week (Mon–Mon)' }).closest('label')).toHaveTextContent(/^WeekMon–Mon$/);
    expect(midweek).toBeChecked();
  });

  it('shows the custom-dates message with the contact phone as a tel: link', () => {
    renderStep();
    const message = screen.getByTestId('custom-dates');
    expect(message).toHaveTextContent('For custom date periods, call 02 4455 6677 to arrange.');
    expect(within(message).getByRole('link', { name: '02 4455 6677' })).toHaveAttribute('href', 'tel:0244556677');
  });

  it('hides the custom-dates message without a contact phone', () => {
    renderWithBooking(<DatesAndBoatStep fleet={boats} settings={{ ...settings, contactPhone: null }} requestedBoat={null} onContinue={vi.fn()} />);
    expect(screen.queryByTestId('custom-dates')).not.toBeInTheDocument();
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
    const cards = screen.getAllByRole('button').filter((c) => c.dataset.slug);
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

  it('are toggle buttons with at most one pressed, and clicking the selected card again deselects it', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda'));
    expect(screen.getByTestId('boat-kalinda')).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(screen.getAllByRole('button', { pressed: true })).toEqual([screen.getByTestId('boat-misty-blue')]);
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(currentState().selectedSlug).toBeNull();
    expect(screen.queryAllByRole('button', { pressed: true })).toHaveLength(0);
    expect(screen.getByTestId('live-region')).toHaveTextContent('No boat selected.');
  });

  it('are operated with Tab and Enter/Space, and announce the boat', async () => {
    renderStep();
    const user = userEvent.setup();
    screen.getByTestId('boat-kalinda').focus();
    await user.tab();
    expect(screen.getByTestId('boat-pacific-blue')).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(currentState().selectedSlug).toBe('pacific-blue');
    expect(screen.getByTestId('live-region')).toHaveTextContent('Selected boat: Pacific Blue.');
    await user.keyboard(' ');
    expect(currentState().selectedSlug).toBeNull();
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

describe('Dates & boat: small screen', () => {
  const period = { start: '2026-10-12', end: '2026-10-16' };
  const bar = () => within(screen.getByTestId('step-action-bar'));
  const barBoat = () => screen.getByTestId('bar-boat');
  const expander = () => screen.getByTestId('boat-expander');
  const toggle = () => bar().getByRole('button', { name: /See other boats|Choose a boat|Hide boats/ });

  it('has no bar above 768px', () => {
    renderStep();
    expect(screen.queryByTestId('step-action-bar')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Boats' })).toBeInTheDocument();
  });

  it('shows the selected boat, Base hire rate and Continue in the bar, and the boat cards only in the expander', async () => {
    setSmallScreen(true);
    renderStep({ selectedSlug: 'kalinda', period });
    expect(barBoat()).toHaveTextContent('Kalinda12 berth');
    expect(within(barBoat()).getByRole('button', { name: 'See other boats' })).toBeInTheDocument();
    await waitFor(() => expect(bar().getByTestId('base-hire-rate')).toHaveTextContent('$2,535.00'));
    expect(bar().getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Continue', hidden: true })).toHaveLength(1);
    expect(screen.getAllByTestId('base-hire-rate')).toHaveLength(1);
    for (const card of screen.getAllByTestId(/^boat-(?!expander)/)) expect(expander()).toContainElement(card);
    expect(screen.queryByRole('heading', { name: 'Boats' })).not.toBeInTheDocument(); // the closed expander is hidden
  });

  it('says "No boat selected" with Choose a boat when nothing is selected', () => {
    setSmallScreen(true);
    renderStep();
    expect(barBoat()).toHaveTextContent('No boat selected');
    expect(within(barBoat()).getByRole('button', { name: 'Choose a boat' })).toHaveAttribute('aria-expanded', 'false');
  });

  it('shows a boat deselected by a guests change greyed with its reason, until no reason applies', async () => {
    setSmallScreen(true);
    renderStep({ selectedSlug: 'runaway-bear', period });
    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText('Adults'), '3');
    await waitFor(() => expect(currentState().selectedSlug).toBeNull());
    expect(barBoat()).toHaveTextContent('Runaway Bear');
    expect(barBoat()).toHaveTextContent('Too small for 3 guests');
    expect(within(barBoat()).getByRole('button', { name: 'Choose a boat' })).toBeInTheDocument();
    expect(toggle()).toHaveAttribute('aria-expanded', 'false'); // never opens by itself
    await user.selectOptions(screen.getByLabelText('Adults'), '2');
    expect(barBoat()).toHaveTextContent('No boat selected');
    expect(currentState().selectedSlug).toBeNull();
  });

  it('shows a boat deselected by a feature change, cleared once a boat is deselected by hand', async () => {
    setSmallScreen(true);
    renderStep({ selectedSlug: 'kalinda', period });
    const user = userEvent.setup();
    await user.click(screen.getByRole('checkbox', { name: 'Spa' }));
    await waitFor(() => expect(barBoat()).toHaveTextContent("KalindaDoesn't match your filters"));
    await user.click(toggle());
    await user.click(screen.getByTestId('boat-pacific-blue'));
    expect(barBoat()).toHaveTextContent('Pacific Blue');
    await user.click(toggle());
    await user.click(screen.getByTestId('boat-pacific-blue')); // deselect by hand
    expect(currentState().deselectedSlug).toBeNull();
    expect(barBoat()).toHaveTextContent('No boat selected');
  });

  it('shows the boat taken at Payment as not available for the dates', async () => {
    apiState.unavailable = { kalinda: [period] };
    setSmallScreen(true);
    renderStep({ deselectedSlug: 'kalinda', period });
    await waitFor(() => expect(barBoat()).toHaveTextContent('KalindaNot available for selected dates'));
  });

  it('shows a greyed requested boat with its reason', () => {
    setSmallScreen(true);
    renderStep({}, details('old-boat', { name: 'Old Boat', isActive: false, berth: 10 }));
    expect(barBoat()).toHaveTextContent('Old BoatNot available for online booking');
  });

  it('opens the expander with the toggle, focusing its heading, and closes it with the toggle and Esc', async () => {
    setSmallScreen(true);
    renderStep();
    const user = userEvent.setup();
    await user.click(toggle());
    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
    expect(toggle()).toHaveTextContent('Hide boats');
    expect(toggle()).toHaveAttribute('aria-controls', expander().id);
    expect(screen.getByRole('heading', { name: 'Boats' })).toHaveFocus();
    expect(document.body.style.overflow).toBe('hidden');
    await user.click(toggle());
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(toggle()).toHaveFocus();
    expect(document.body.style.overflow).toBe('');
    await user.click(toggle());
    await user.keyboard('{Escape}');
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(toggle()).toHaveFocus();
  });

  it('closes when a card selects a boat, and stays open when the selected card is deselected', async () => {
    setSmallScreen(true);
    renderStep({ selectedSlug: 'kalinda', period });
    const user = userEvent.setup();
    await waitFor(() => expect(bar().getByTestId('base-hire-rate')).toHaveTextContent('$2,535.00'));
    await user.click(toggle());
    await user.click(screen.getByTestId('boat-kalinda'));
    expect(currentState().selectedSlug).toBeNull();
    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
    expect(bar().getByTestId('base-hire-rate')).toHaveTextContent('Select dates and a boat'); // live while open
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(currentState().selectedSlug).toBe('misty-blue');
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(toggle()).toHaveTextContent('See other boats');
    expect(toggle()).toHaveFocus();
  });

  it('closes the expander when Continue is pressed', async () => {
    setSmallScreen(true);
    const { onContinue } = renderStep({ selectedSlug: 'kalinda', adults: 2, period });
    const user = userEvent.setup();
    await waitFor(() => expect(bar().getByRole('button', { name: 'Continue' })).toBeEnabled());
    await user.click(toggle());
    await user.click(bar().getByRole('button', { name: 'Continue' }));
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(onContinue).toHaveBeenCalledTimes(1);
  });
});
