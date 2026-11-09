import { useEffect, useId, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Dialog } from '@shared/components/Dialog/Dialog';
import { addDays, addMonths, monthGrid, startOfMonth, type IsoDate } from '@shared/utils/dates';
import { formatMoney } from '@shared/utils/money';
import { PERIOD_LABELS, type OnlinePeriodType, type Stay } from '@shared/utils/periods';
import { Notice } from '@/components/Notice/Notice';
import { useBoatDetails, usePeriodAvailability } from '@/hooks/useFleet';
import { keys } from '@/lib/api';
import { useBooking } from '@/lib/booking-state';
import type { BoatDetails, BoatSummary, BookingSettings } from '@/lib/types';
import { useFleetAvailability } from '../api/queries';
import {
  availabilityWindow, blockedPeriodFor, calendarBoats, candidateBoats, checkInOutLine, maxBerth, requestedBoatReason,
  selectedBoatEligible, selectedLine, type CardBoat,
} from '../utils/availability';
import { roomingWarningText } from '../utils/rooming';
import { BoatCards } from './BoatCards';
import { Calendar } from './Calendar';
import styles from './DatesAndBoatStep.module.css';

export interface DatesAndBoatStepProps {
  fleet: BoatSummary[];
  settings: BookingSettings;
  requestedBoat: BoatDetails | null;
  onContinue: () => void;
}

const PERIOD_TYPES: OnlinePeriodType[] = ['Midweek', 'Weekend', 'Week'];

export function DatesAndBoatStep({ fleet, settings, requestedBoat, onContinue }: DatesAndBoatStepProps) {
  const { state, dispatch } = useBooking();
  const queryClient = useQueryClient();
  const ids = { adults: useId(), children: useId() };
  const filters = { adults: state.adults, children: state.children, spa: state.spa, pets: state.pets };
  const earliest = addDays(settings.today, settings.minimumLeadTimeDays);
  const [month, setMonth] = useState<IsoDate>(startOfMonth(state.period?.start ?? (earliest > settings.today ? earliest : settings.today)));
  const [enquire, setEnquire] = useState<string | null>(null);
  const [rooming, setRooming] = useState<string | null>(null);

  // Coming back after the Payment step (or a 409) fetches availability again.
  useEffect(() => {
    if (!state.reachedPayment) return;
    void queryClient.resetQueries({ queryKey: keys.availabilityRoot });
    void queryClient.resetQueries({ queryKey: keys.periodRoot });
    dispatch({ type: 'cachesCleared' });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const grid = monthGrid(month);
  const range = availabilityWindow(grid);
  const availability = useFleetAvailability(range.from, range.to);
  const periodResult = usePeriodAvailability(state.periodType, state.period);
  const selectedDetails = useBoatDetails(state.selectedSlug);

  const candidates = candidateBoats(fleet, filters);
  const boatsForCalendar = calendarBoats(candidates, fleet, state.selectedSlug, state.showSelectedOnly);

  // Keep the selection valid: a boat that no longer fits the period, guests or features is deselected (the period is kept).
  useEffect(() => {
    if (state.selectedSlug && !selectedBoatEligible(state.selectedSlug, fleet, filters, periodResult.data)) {
      dispatch({ type: 'selectBoat', slug: null });
    }
  }, [state.selectedSlug, state.adults, state.children, state.spa, state.pets, periodResult.data, fleet]); // eslint-disable-line react-hooks/exhaustive-deps

  const requested: CardBoat | null = requestedBoat
    ? { slug: requestedBoat.slug, name: requestedBoat.name, imageUrl: requestedBoat.imageUrl, berth: requestedBoat.berth,
        hasSpa: requestedBoat.hasSpa, isPetFriendly: requestedBoat.isPetFriendly, isActive: requestedBoat.isActive }
    : null;
  const requestedReason = requested ? requestedBoatReason(requested, filters, periodResult.data) : null;
  const freeFor = (slug: string) => periodResult.data?.boats.find((b) => b.slug === slug)?.available ?? false;
  const periodKnown = !!state.period && !!periodResult.data;
  const others = candidates
    .filter((b) => b.slug !== requested?.slug)
    .filter((b) => !periodKnown || freeFor(b.slug))
    .map((b) => ({ ...b, isActive: true }));
  let emptyMessage: string | null = null;
  if (periodKnown && others.length === 0) {
    emptyMessage = candidates.some((b) => freeFor(b.slug)) ? 'No other boats are available for these dates.' : 'No boats match your filters for these dates.';
  }
  const prices = Object.fromEntries((periodResult.data?.boats ?? []).map((b) => [b.slug, b.hirePrice]));
  const selectedBoat = fleet.find((b) => b.slug === state.selectedSlug) ?? null;
  const hirePrice = state.selectedSlug && periodResult.data ? prices[state.selectedSlug] : undefined;

  const maxGuests = maxBerth(fleet);
  const setAdults = (adults: number) => dispatch({ type: 'guests', adults, children: Math.min(state.children, maxGuests - adults) });

  const selectPeriod = (period: Stay) => dispatch({ type: 'period', period });
  const showEnquire = (period: Stay) => {
    const blocked = blockedPeriodFor(availability.data, period);
    setEnquire(`Bookings over ${blocked?.name ?? 'this period'} must be made by phone or email: ${settings.contactPhone ?? ''} / ${settings.contactEmail ?? ''}.`);
  };

  const continueClicked = () => {
    const details = selectedDetails.data;
    if (!details || !state.period) return;
    const total = state.adults + state.children;
    const accepted = state.rooming && state.rooming.slug === details.slug && state.rooming.adults === state.adults && state.rooming.children === state.children;
    if (total > details.adultBeds + details.kidsBeds && !accepted) {
      setRooming(roomingWarningText(details, total));
      return;
    }
    onContinue();
  };

  const acceptRooming = () => {
    if (!rooming || !state.selectedSlug) return;
    dispatch({ type: 'acceptRooming', acceptance: { slug: state.selectedSlug, adults: state.adults, children: state.children, text: rooming } });
    setRooming(null);
    onContinue();
  };

  const announce = [state.period ? selectedLine(state.period) : '', selectedBoat ? `Selected boat: ${selectedBoat.name}.` : ''].filter(Boolean).join(' ');

  return (
    <section className={styles.step} aria-labelledby="dates-heading">
      <h2 id="dates-heading" className={styles.heading}>Choose your dates and boat</h2>
      {state.notice && <Notice tone="warning">{state.notice}</Notice>}
      <div className={styles.columns}>
        <div className={styles.left}>
          <fieldset className={styles.fieldset}>
            <legend className={styles.label}>Type of stay</legend>
            <div className={styles.periods}>
              {PERIOD_TYPES.map((t) => (
                <label key={t} className={`${styles.period} ${state.periodType === t ? styles.periodChecked : ''}`}>
                  <input type="radio" name="periodType" value={t} checked={state.periodType === t} onChange={() => dispatch({ type: 'periodType', periodType: t })} />
                  <span>
                    <strong>{PERIOD_LABELS[t].name}</strong> <span className={styles.muted}>({PERIOD_LABELS[t].days})</span>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          <p className={styles.times} data-testid="check-in-line">{checkInOutLine(state.period, month)}</p>

          <Calendar
            month={month}
            canGoBack={month > startOfMonth(settings.today)}
            canGoForward={month < startOfMonth(settings.bookingsOpenUntil)}
            onMonthChange={(d) => setMonth(addMonths(month, d))}
            periodType={state.periodType}
            period={state.period}
            availability={availability.data}
            loading={availability.isLoading}
            boats={boatsForCalendar}
            settings={settings}
            onSelect={selectPeriod}
            onEnquire={showEnquire}
          />
          {availability.isError && <Notice tone="error">We couldn't load availability. Please try again.</Notice>}

          {state.period && <p className={styles.selectedLine} data-testid="selected-line">{selectedLine(state.period)}</p>}
          <div className="visually-hidden" aria-live="polite" data-testid="live-region">{announce}</div>

          <label className={styles.check}>
            <input
              type="checkbox"
              checked={state.showSelectedOnly}
              disabled={!state.selectedSlug}
              onChange={(e) => dispatch({ type: 'showSelectedOnly', value: e.target.checked })}
            />
            {selectedBoat ? `Show ${selectedBoat.name}'s availability only` : "Show the selected boat's availability only"}
          </label>

          <div className={styles.filters}>
            <h3 className={styles.subheading}>Guests and features</h3>
            <div className={styles.guestRow}>
              <div className={styles.select}>
                <label htmlFor={ids.adults}>Adults</label>
                <select id={ids.adults} value={state.adults} onChange={(e) => setAdults(Number(e.target.value))}>
                  {Array.from({ length: maxGuests }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </div>
              <div className={styles.select}>
                <label htmlFor={ids.children}>Children (Under 12)</label>
                <select id={ids.children} value={state.children} onChange={(e) => dispatch({ type: 'guests', adults: state.adults, children: Number(e.target.value) })}>
                  {Array.from({ length: maxGuests - state.adults + 1 }, (_, i) => i).map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </div>
            </div>
            <div className={styles.featureRow}>
              <label className={styles.check}>
                <input type="checkbox" checked={state.spa} onChange={(e) => dispatch({ type: 'filter', spa: e.target.checked })} /> Spa
              </label>
              <label className={styles.check}>
                <input type="checkbox" checked={state.pets} onChange={(e) => dispatch({ type: 'filter', pets: e.target.checked })} /> Pet friendly
              </label>
            </div>
          </div>

          <div className={styles.rate} data-testid="base-hire-rate">
            <div>
              <div className={styles.rateLabel}>Base hire rate</div>
              <div className={styles.rateSub}>Excludes add-ons</div>
            </div>
            <div className={styles.rateValue}>
              {hirePrice !== undefined && hirePrice !== null && state.period ? formatMoney(hirePrice) : 'Select dates and a boat'}
            </div>
          </div>

          <div className={styles.actions}>
            <Button variant="primary" onClick={continueClicked} disabled={!state.period || !state.selectedSlug || !selectedDetails.data}>
              Continue
            </Button>
          </div>
        </div>

        <div className={styles.right}>
          <h3 className={styles.label}>Boats</h3>
          <BoatCards
            requested={requested}
            requestedReason={requestedReason}
            others={others}
            emptyMessage={emptyMessage}
            selectedSlug={state.selectedSlug}
            prices={state.period ? prices : {}}
            onSelect={(slug) => dispatch({ type: 'selectBoat', slug })}
          />
        </div>
      </div>

      <Dialog open={!!enquire} title="Please call or email us" onClose={() => setEnquire(null)} size="sm"
        footer={<Button variant="primary" onClick={() => setEnquire(null)}>OK</Button>}>
        <p>{enquire}</p>
      </Dialog>

      <Dialog open={!!rooming} title="Sleeping arrangements" onClose={() => setRooming(null)} size="sm"
        footer={<><Button onClick={() => setRooming(null)}>Cancel</Button><Button variant="primary" onClick={acceptRooming}>Accept</Button></>}>
        <p>{rooming}</p>
      </Dialog>
    </section>
  );
}
