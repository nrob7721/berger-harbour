import { describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { apiState, boats, details, settings } from '@/testing/server';
import { currentState, renderWithBooking } from '@/testing/render';
import { setSmallScreen } from '@/testing/screen-size';
import type { BookingState } from '@/lib/booking-state';
import type { BoatDetails } from '@/lib/types';
import { DatesAndBoatStep } from './DatesAndBoatStep';

/** Renders the step with Mid-week chosen unless the state says otherwise (a fresh start has none, decision 44). */
function renderStep(state: Partial<BookingState> = {}, requested: BoatDetails | null = null, onContinue = vi.fn()) {
  const r = renderWithBooking(<DatesAndBoatStep fleet={boats} settings={settings} requestedBoat={requested} onContinue={onContinue} />,
    { typeOfStay: 'Midweek', ...state });
  return { ...r, onContinue };
}

/** A card's inner toggle button, which carries `aria-pressed` (§11.2). */
const toggleOf = (slug: string) => screen.getByTestId(`boat-${slug}`).querySelector<HTMLButtonElement>('button[aria-pressed]')!;
const cardSlugs = () => Array.from(document.querySelectorAll<HTMLElement>('[data-boat-card]')).map((c) => c.dataset.slug);

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

  it('shows the check-in line with one zone label, the check-out date\'s', async () => {
    renderStep();
    expect(screen.getByTestId('check-in-line')).toHaveTextContent('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    expect(screen.getByTestId('check-in-line')).toHaveTextContent('Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)');
  });

  it('labels a stay that crosses a daylight-saving change with its check-out date\'s zone', () => {
    // Daylight saving ends on Sun 04/04/2027.
    renderStep({ typeOfStay: 'WeekFri', period: { start: '2027-04-02', end: '2027-04-09' } });
    expect(screen.getByTestId('check-in-line')).toHaveTextContent(/^Check-in 11am – 2pm · Check-out from 8am \(AEST\/NSW\)$/);
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
    expect(toggleOf('kalinda')).toHaveAttribute('aria-pressed', 'true');
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
    expect(cardSlugs()).toEqual(['misty-blue', 'kalinda', 'pacific-blue', 'runaway-bear']);
    expect(screen.getByText('Other boats available')).toBeInTheDocument();
    expect(toggleOf('misty-blue')).toHaveTextContent('Misty Blue9 berthPet friendly');
  });

  it('greys an inactive requested boat with its reason and selects nothing', () => {
    renderStep({}, details('old-boat', { name: 'Old Boat', isActive: false, berth: 10 }));
    const card = screen.getByTestId('boat-old-boat');
    expect(toggleOf('old-boat')).toHaveAttribute('aria-disabled', 'true');
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

  it('have inner toggle buttons with at most one pressed, and clicking the selected card again deselects it', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda'));
    expect(toggleOf('kalinda')).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(screen.getAllByRole('button', { pressed: true })).toEqual([toggleOf('misty-blue')]);
    await user.click(screen.getByTestId('boat-misty-blue'));
    expect(currentState().selectedSlug).toBeNull();
    expect(screen.queryAllByRole('button', { pressed: true })).toHaveLength(0);
    expect(screen.getByTestId('live-region')).toHaveTextContent('No boat selected.');
  });

  it('are operated with Tab and Enter/Space, and announce the boat', async () => {
    renderStep();
    const user = userEvent.setup();
    toggleOf('kalinda').focus();
    await user.tab();
    expect(toggleOf('pacific-blue')).toHaveFocus();
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
    expect(screen.getByTestId('base-hire-rate')).toHaveTextContent(/^Base hire rateExcludes add-onsSelect dates$/);
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    expect(screen.getByTestId('base-hire-rate')).toHaveTextContent(/Select a boat$/);
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
    expect(bar().getByTestId('base-hire-rate')).toHaveTextContent(/Select a boat$/); // live while open
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

/** A touch swipe on `el`: `dx` sideways and `dy` up or down. */
function swipe(el: Element, dx: number, dy = 0) {
  fireEvent.touchStart(el, { touches: [{ clientX: 200, clientY: 200 }] });
  fireEvent.touchEnd(el, { changedTouches: [{ clientX: 200 + dx, clientY: 200 + dy }] });
}

const monthShown = () => screen.getByRole('grid').getAttribute('aria-label');

describe('Dates & boat: type of stay', () => {
  it('starts with none checked and the calendar under an inert overlay', async () => {
    renderStep({ typeOfStay: null });
    for (const radio of screen.getAllByRole('radio')) expect(radio).not.toBeChecked();
    expect(screen.getByTestId('calendar-overlay')).toHaveTextContent('Select a type of stay to view availability');
    expect(screen.getByTestId('calendar')).toHaveAttribute('inert');
    expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled();
    expect(screen.queryByTestId('selected-line')).not.toBeInTheDocument();
    expect(screen.getByTestId('check-in-line')).toHaveTextContent('Check-in 11am – 2pm · Check-out from 8am (AEST/NSW)');
    // Availability for the visible month is still fetched, so the calendar is ready once a type is chosen.
    await waitFor(() => expect(apiState.calls.some((c) => c.path === '/api/availability')).toBe(true));
  });

  it('ignores day clicks, keys and swipes under the overlay', async () => {
    renderStep({ typeOfStay: null });
    await waitFor(() => expect(screen.getByRole('grid')).toHaveAttribute('aria-busy', 'false'));
    const user = userEvent.setup();
    const monday = day('2026-08-31');
    expect(monday).toHaveAttribute('tabindex', '-1');
    await user.click(monday);
    fireEvent.keyDown(monday, { key: 'Enter' });
    swipe(screen.getByTestId('calendar'), -120);
    expect(currentState().period).toBeNull();
    expect(monthShown()).toBe('August 2026');
  });

  it('keeps the boat cards and filters working', async () => {
    renderStep({ typeOfStay: null });
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda'));
    expect(currentState().selectedSlug).toBe('kalinda');
    await user.click(screen.getByRole('checkbox', { name: 'Pet friendly' }));
    expect(currentState().pets).toBe(true);
  });

  it('removes the overlay once a type is checked, which can then be changed but not cleared', async () => {
    renderStep({ typeOfStay: null });
    const user = userEvent.setup();
    const weekend = screen.getByRole('radio', { name: 'Weekend (Fri–Mon)' });
    await user.click(weekend);
    expect(weekend).toBeChecked();
    expect(screen.queryByTestId('calendar-overlay')).not.toBeInTheDocument();
    expect(screen.getByTestId('calendar')).not.toHaveAttribute('inert');
    await user.click(weekend);
    expect(weekend).toBeChecked();
    expect(currentState().typeOfStay).toBe('Weekend');
    await user.click(screen.getByRole('radio', { name: 'Week (Mon–Mon)' }));
    expect(currentState().typeOfStay).toBe('WeekMon');
    expect(screen.queryByTestId('calendar-overlay')).not.toBeInTheDocument();
  });

  it('restores a saved type of stay with no overlay', () => {
    renderStep({ typeOfStay: 'WeekFri' });
    expect(screen.getByRole('radio', { name: 'Week (Fri–Fri)' })).toBeChecked();
    expect(screen.queryByTestId('calendar-overlay')).not.toBeInTheDocument();
  });
});

describe('Dates & boat: calendar swipe', () => {
  it('shows the next month on a left swipe and the previous on a right swipe, within the month limits', async () => {
    renderStep();
    const calendar = screen.getByTestId('calendar');
    swipe(calendar, 80); // right at the current month: no earlier month
    expect(monthShown()).toBe('August 2026');
    swipe(calendar, -80);
    expect(monthShown()).toBe('September 2026');
    swipe(calendar, -60);
    expect(monthShown()).toBe('October 2026');
    swipe(calendar, 60);
    expect(monthShown()).toBe('September 2026');
    expect(currentState().period).toBeNull(); // a swipe never selects a day
  });

  it('does nothing for a short or mostly vertical movement', () => {
    renderStep();
    const calendar = screen.getByTestId('calendar');
    swipe(calendar, -49);
    swipe(calendar, -80, 120);
    expect(monthShown()).toBe('August 2026');
  });

  it('stops at the month of "bookings open until"', () => {
    renderStep({ typeOfStay: 'Midweek', period: { start: '2027-11-22', end: '2027-11-26' } });
    expect(monthShown()).toBe('November 2027');
    swipe(screen.getByTestId('calendar'), -100);
    expect(monthShown()).toBe('November 2027');
    expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled();
  });
});

describe('Dates & boat: views', () => {
  const columns = () => screen.getByTestId('dates-columns');
  const view = () => columns().dataset.view;
  const boatsColumn = () => screen.getByRole('group', { name: 'Show boat details' });

  it('opens in the Booking details view with compact cards: no tags and no More details', () => {
    renderStep();
    expect(view()).toBe('details');
    expect(screen.queryByRole('button', { name: 'More details' })).not.toBeInTheDocument();
    expect(within(screen.getByTestId('boat-kalinda')).queryByText('Sleeps 12')).not.toBeInTheDocument();
  });

  it('switches with a click on the inactive column\'s background, and back', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(screen.getByRole('heading', { name: 'Boats' }));
    expect(view()).toBe('boats');
    expect(within(screen.getByTestId('boat-kalinda')).getByText('Sleeps 12')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'More details' })).toHaveLength(boats.length);
    await user.click(screen.getByRole('heading', { name: 'Guests and features' }));
    expect(view()).toBe('details');
  });

  it('lets the inactive column\'s controls do their job without switching', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(screen.getByTestId('boat-kalinda')); // a card in the Booking details view
    expect(currentState().selectedSlug).toBe('kalinda');
    expect(view()).toBe('details');
    await user.click(boatsColumn());
    expect(view()).toBe('boats');
    await user.click(screen.getByRole('checkbox', { name: 'Spa' })); // a filter in the Boats view
    await user.click(screen.getByRole('radio', { name: 'Weekend (Fri–Mon)' }));
    await goToOctober(user);
    await user.click(day('2026-10-12'));
    expect(currentState().period).toEqual({ start: '2026-10-09', end: '2026-10-12' });
    expect(view()).toBe('boats');
  });

  it('never switches when a period or a boat is selected', async () => {
    renderStep();
    const user = await goToOctober();
    await user.click(day('2026-10-14'));
    await user.click(screen.getByTestId('boat-pacific-blue'));
    expect(view()).toBe('details');
  });

  it('puts the inactive column in the tab order, switching with Enter or Space and keeping focus on it', async () => {
    renderStep();
    const user = userEvent.setup();
    const column = boatsColumn();
    expect(column).toHaveAttribute('tabindex', '0');
    column.focus();
    await user.keyboard('{Enter}');
    expect(view()).toBe('boats');
    expect(column).toHaveFocus();
    expect(column).toHaveAttribute('tabindex', '-1'); // out of the tab order once active
    const details = screen.getByRole('group', { name: 'Show booking details' });
    details.focus();
    await user.keyboard(' ');
    expect(view()).toBe('details');
    expect(details).toHaveFocus();
  });

  it('closes every More details when switching to the Booking details view', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(boatsColumn());
    const [first, second] = screen.getAllByRole('button', { name: 'More details' });
    await user.click(first);
    await user.click(second);
    expect(screen.getAllByRole('button', { name: 'Hide details' })).toHaveLength(2);
    await user.click(screen.getByRole('heading', { name: 'Guests and features' }));
    await user.click(screen.getByRole('heading', { name: 'Boats' }));
    expect(screen.queryByRole('button', { name: 'Hide details' })).not.toBeInTheDocument();
  });

  it('opens in the Booking details view again on a new visit to the step', async () => {
    const first = renderStep();
    await userEvent.setup().click(boatsColumn());
    first.unmount();
    renderStep({ period: { start: '2026-10-12', end: '2026-10-16' }, selectedSlug: 'kalinda' });
    expect(view()).toBe('details');
  });

  it('has no views on a small screen', () => {
    setSmallScreen(true);
    renderStep();
    expect(view()).toBeUndefined();
    expect(screen.queryByRole('group', { name: /Show (boat|booking) details/ })).not.toBeInTheDocument();
  });
});

describe('Dates & boat: detailed boat cards and More details', () => {
  async function boatsView(state: Partial<BookingState> = {}, requested: BoatDetails | null = null) {
    renderStep(state, requested);
    const user = userEvent.setup();
    await user.click(screen.getByRole('heading', { name: 'Boats' }));
    return user;
  }
  const card = (slug: string) => within(screen.getByTestId(`boat-${slug}`));
  const openDetails = async (user: ReturnType<typeof userEvent.setup>, slug: string) => {
    await user.click(card(slug).getByRole('button', { name: 'More details' }));
    return card(slug);
  };

  it('toggle the selection with a click anywhere outside their controls, and show the first 5 tags then "+n more"', async () => {
    const user = await boatsView();
    expect(card('kalinda').getAllByRole('listitem').map((t) => t.textContent))
      .toEqual(['Sleeps 12', '3 double bedrooms', 'Bunk room', 'Air conditioning', 'Generator', '+2 more']);
    await user.click(card('kalinda').getByText('Generator'));
    expect(toggleOf('kalinda')).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByTestId('boat-kalinda').querySelector('img')!);
    expect(currentState().selectedSlug).toBeNull();
  });

  it('open More details without selecting, and select without opening; several can be open', async () => {
    const user = await boatsView();
    const kalinda = await openDetails(user, 'kalinda');
    const button = kalinda.getByRole('button', { name: 'Hide details' });
    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(button.getAttribute('aria-controls')).toBeTruthy();
    expect(currentState().selectedSlug).toBeNull();
    await user.click(screen.getByTestId('boat-pacific-blue'));
    expect(currentState().selectedSlug).toBe('pacific-blue');
    expect(card('pacific-blue').getByRole('button', { name: 'More details' })).toHaveAttribute('aria-expanded', 'false');
    await openDetails(user, 'pacific-blue');
    expect(screen.getAllByRole('button', { name: 'Hide details' })).toHaveLength(2);
    await user.click(kalinda.getByRole('button', { name: 'Hide details' }));
    expect(kalinda.getByRole('button', { name: 'More details' })).toBeInTheDocument();
  });

  it('show the photo reel in order with its counter and captions, loading only the current and next photo', async () => {
    const user = await boatsView();
    const pacific = await openDetails(user, 'pacific-blue');
    const reel = within(await pacific.findByTestId('photo-reel'));
    expect(reel.getByTestId('photo-counter')).toHaveTextContent('1 / 4');
    expect(reel.getByRole('img', { name: 'Pacific Blue photo 1' })).toBeVisible();
    expect(reel.queryByTestId('photo-caption')).not.toBeInTheDocument();
    expect(reel.getAllByRole('img', { hidden: true })).toHaveLength(2); // the current photo and the next one
    expect(reel.getByRole('button', { name: 'Previous photo' })).toBeDisabled();
    await user.click(reel.getByRole('button', { name: 'Next photo' }));
    expect(reel.getByTestId('photo-counter')).toHaveTextContent('2 / 4');
    expect(reel.getAllByRole('img', { hidden: true })).toHaveLength(3); // one more ahead
    await user.click(reel.getByRole('button', { name: 'Next photo' }));
    expect(reel.getByRole('img', { name: 'Upper deck with a heated spa' })).toBeVisible();
    expect(reel.getByTestId('photo-caption')).toHaveTextContent('Upper deck with a heated spa');
    await user.click(reel.getByRole('button', { name: 'Next photo' }));
    expect(reel.getByTestId('photo-counter')).toHaveTextContent('4 / 4');
    expect(reel.getByRole('button', { name: 'Next photo' })).toBeDisabled();
    expect(currentState().selectedSlug).toBeNull(); // the reel's buttons never select the boat
  });

  it('change photo with a swipe on the reel, stopping at the first and last photos', async () => {
    const user = await boatsView();
    const pacific = await openDetails(user, 'pacific-blue');
    const reel = await pacific.findByTestId('photo-reel');
    const viewport = reel.firstElementChild!;
    swipe(viewport, 70);
    expect(within(reel).getByTestId('photo-counter')).toHaveTextContent('1 / 4');
    swipe(viewport, -70);
    swipe(viewport, -70);
    expect(within(reel).getByTestId('photo-counter')).toHaveTextContent('3 / 4');
    swipe(viewport, 70);
    expect(within(reel).getByTestId('photo-counter')).toHaveTextContent('2 / 4');
    swipe(viewport, -30);
    expect(within(reel).getByTestId('photo-counter')).toHaveTextContent('2 / 4');
  });

  it('show the placeholder alone for a boat without photos', async () => {
    const user = await boatsView();
    const bear = await openDetails(user, 'runaway-bear');
    const reel = within(await bear.findByTestId('photo-reel'));
    expect(reel.getByRole('img', { name: 'Runaway Bear photo (no photo)' })).toBeInTheDocument();
    expect(reel.queryByRole('button')).not.toBeInTheDocument();
    expect(reel.queryByTestId('photo-counter')).not.toBeInTheDocument();
  });

  it('show the bedding and the prices table by season, with "—" for a missing rate, then the deposit and bond', async () => {
    const user = await boatsView();
    const kalinda = await openDetails(user, 'kalinda');
    const panel = within(await kalinda.findByTestId('boat-details'));
    expect(panel.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Features', 'Prices', 'Bedding']);
    expect(panel.getAllByRole('listitem').map((t) => t.textContent)).toEqual(['Sleeps 12', '3 double bedrooms', 'Bunk room', 'Air conditioning', 'Generator', 'Upper-deck BBQ', 'Esky']);
    await user.click(panel.getByRole('tab', { name: 'Bedding' }));
    expect(panel.getByText('4 queens and bunks')).toBeInTheDocument();
    expect(currentState().selectedSlug).toBeNull(); // the tabs never select the boat
    await user.click(panel.getByRole('tab', { name: 'Prices' }));
    const table = panel.getByTestId('prices-table');
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Season', 'Midweek', 'Weekend', 'Week', 'Long Weekend']);
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((r) => within(r).getByRole('rowheader').textContent)).toEqual(['NormalAll other dates', 'Off Peak01/05 – 31/08', 'Peak01/12 – 31/01']);
    expect(within(rows[0]).getAllByRole('cell').map((c) => c.textContent)).toEqual(['$2,535.00', '$2,545.00', '$2,555.00', '$2,565.00']);
    expect(within(rows[2]).getAllByRole('cell').map((c) => c.textContent)).toEqual(['$2,735.00', '$2,745.00', '$2,755.00', '—']);
    expect(panel.getByText('Long weekends are booked by phone.')).toBeInTheDocument();
    expect(panel.getByText('Deposit $1,000.00 · Security bond $2,000.00')).toBeInTheDocument();
  });

  it('show a spinner while the boat details are loading, with the tags straight away', async () => {
    apiState.fleetDetailsDelay = 400;
    const user = await boatsView();
    const kalinda = await openDetails(user, 'kalinda');
    expect(kalinda.getByRole('status', { name: 'Loading boat details' })).toBeInTheDocument();
    expect(kalinda.getByText('Sleeps 12')).toBeInTheDocument();
    expect(await kalinda.findByTestId('photo-reel', {}, { timeout: 2000 })).toBeInTheDocument();
  });

  it('show an error with Retry when the boat details failed', async () => {
    apiState.fleetDetails = 'error';
    const user = await boatsView();
    const kalinda = await openDetails(user, 'kalinda');
    expect(await kalinda.findByText("Boat details couldn't be loaded.")).toBeInTheDocument();
    apiState.fleetDetails = 'ok';
    await user.click(kalinda.getByRole('button', { name: 'Retry' }));
    expect(await kalinda.findByTestId('photo-reel')).toBeInTheDocument();
    expect(currentState().selectedSlug).toBeNull();
  });

  it('open More details on a greyed requested boat, from its own data', async () => {
    const old = details('old-boat', {
      name: 'Old Boat', isActive: false, berth: 10, tags: ['Retired'],
      images: [{ url: 'https://bergerhouseboats.com.au/wp-content/uploads/old.jpg', description: 'The old boat' }],
    });
    const user = await boatsView({}, old);
    const card = await openDetails(user, 'old-boat');
    expect(await card.findByRole('img', { name: 'The old boat' })).toBeInTheDocument();
    expect(card.getByText('Retired')).toBeInTheDocument();
    expect(toggleOf('old-boat')).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('Dates & boat: small-screen detailed cards', () => {
  const toggle = () => within(screen.getByTestId('step-action-bar')).getByRole('button', { name: /See other boats|Choose a boat|Hide boats/ });

  it('show the first 5 tags and "+{n} more", which opens More details at its Features tab', async () => {
    setSmallScreen(true);
    renderStep();
    const user = userEvent.setup();
    await user.click(toggle());
    const kalinda = within(screen.getByTestId('boat-kalinda'));
    expect(kalinda.getAllByRole('listitem').map((t) => t.textContent))
      .toEqual(['Sleeps 12', '3 double bedrooms', 'Bunk room', 'Air conditioning', 'Generator', '+2 more']);
    await user.click(kalinda.getByRole('button', { name: '+2 more' }));
    expect(kalinda.getByRole('button', { name: 'Hide details' })).toHaveAttribute('aria-expanded', 'true');
    expect(kalinda.getByRole('tab', { name: 'Features' })).toHaveAttribute('aria-selected', 'true');
    expect(kalinda.getAllByRole('listitem')).toHaveLength(7);
    expect(kalinda.queryByRole('button', { name: /more$/ })).not.toBeInTheDocument();
    expect(currentState().selectedSlug).toBeNull();
    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
  });

  it('keep the expander open while More details opens and closes', async () => {
    setSmallScreen(true);
    renderStep();
    const user = userEvent.setup();
    await user.click(toggle());
    const pacific = within(screen.getByTestId('boat-pacific-blue'));
    await user.click(pacific.getByRole('button', { name: 'More details' }));
    expect(await pacific.findByTestId('photo-reel')).toBeInTheDocument();
    await user.click(pacific.getByRole('button', { name: 'Hide details' }));
    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
    expect(currentState().selectedSlug).toBeNull();
  });
});

describe('Dates & boat: photo reel on closed detailed cards', () => {
  it('shows the photo reel instead of the thumbnail once the details have arrived, without opening More details', async () => {
    renderStep();
    const user = userEvent.setup();
    await user.click(screen.getByRole('heading', { name: 'Boats' }));
    const pacific = within(screen.getByTestId('boat-pacific-blue'));
    const reel = within(await pacific.findByTestId('photo-reel'));
    expect(pacific.getByRole('button', { name: 'More details' })).toHaveAttribute('aria-expanded', 'false');
    await user.click(reel.getByRole('button', { name: 'Next photo' }));
    expect(reel.getByTestId('photo-counter')).toHaveTextContent('2 / 4');
    expect(currentState().selectedSlug).toBeNull();
    await user.click(pacific.getByRole('button', { name: 'More details' }));
    expect(reel.getByTestId('photo-counter')).toHaveTextContent('2 / 4'); // opening keeps the reel where it was
  });
});
