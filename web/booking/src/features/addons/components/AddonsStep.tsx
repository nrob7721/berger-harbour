import { useEffect, useState } from 'react';
import { useMutation, useQueries } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { errorMessage } from '@shared/lib/http';
import { formatMoney } from '@shared/utils/money';
import { Notice } from '@/components/Notice/Notice';
import { StepLayout } from '@/components/StepLayout/StepLayout';
import { useBoatDetails, usePeriodAvailability } from '@/hooks/useFleet';
import { useSmallScreen } from '@/hooks/useSmallScreen';
import { api, keys } from '@/lib/api';
import { isAfter, periodTypeOfState, useBooking } from '@/lib/booking-state';
import { draftBody } from '@/lib/draft';
import { addonPrice, bookingPrice } from '../utils/bookingPrice';
import { addonsRemovedNotice, offeredAddons } from '../utils/continue';
import styles from './AddonsStep.module.css';

/** Add-ons. `onContinued` runs once the draft is updated, so a progress-step jump can carry on from here. */
export function AddonsStep({ onContinued }: { onContinued: () => void }) {
  const { state, dispatch } = useBooking();
  const small = useSmallScreen();
  const boat = useBoatDetails(state.selectedSlug);
  const period = usePeriodAvailability(periodTypeOfState(state), state.period);
  const [removedNotice, setRemovedNotice] = useState<string | null>(null);
  const allowed = boat.data?.addons ?? [];

  // Requested add-ons the (newly) selected boat doesn't offer are removed.
  useEffect(() => {
    if (!boat.data) return;
    const kept = offeredAddons(state.addons, boat.data);
    if (Object.keys(kept).length !== Object.keys(state.addons).length) {
      dispatch({ type: 'addons', addons: kept });
      setRemovedNotice(addonsRemovedNotice(boat.data.name));
    }
  }, [boat.data]); // eslint-disable-line react-hooks/exhaustive-deps

  // Capacity is fetched every time this step is shown, never from cache.
  const capacityAddons = allowed.filter((a) => a.hasCapacity);
  const remaining = useQueries({
    queries: capacityAddons.map((a) => ({
      queryKey: keys.addonRemaining(a.id, state.period!.start, state.period!.end),
      queryFn: () => api.addonRemaining(a.id, state.period!.start, state.period!.end),
      staleTime: 0,
      gcTime: 0,
      refetchOnMount: 'always' as const,
      enabled: !!state.period,
    })),
  });
  const remainingFor = (id: string): number | undefined => {
    const i = capacityAddons.findIndex((a) => a.id === id);
    return i < 0 ? undefined : remaining[i]?.data?.remaining;
  };
  const capacityLoading = remaining.some((r) => r.isLoading);

  // Add-ons with nothing left are hidden, and dropped from the request.
  useEffect(() => {
    let changed = false;
    const next = { ...state.addons };
    for (const a of capacityAddons) {
      const left = remainingFor(a.id);
      if (left === undefined || next[a.id] === undefined) continue;
      if (left === 0) { delete next[a.id]; changed = true; }
      else if (next[a.id] > left) { next[a.id] = left; changed = true; }
    }
    if (changed) dispatch({ type: 'addons', addons: next });
  }, [remaining.map((r) => r.data?.remaining).join(',')]); // eslint-disable-line react-hooks/exhaustive-deps

  const visible = allowed.filter((a) => !a.hasCapacity || (remainingFor(a.id) ?? 1) > 0);
  const hire = state.selectedSlug ? period.data?.boats.find((b) => b.slug === state.selectedSlug)?.hirePrice ?? null : null;
  const price = bookingPrice(hire ?? 0, allowed, state.addons, periodTypeOfState(state)!);

  const setQuantity = (id: string, quantity: number | null) => {
    const next = { ...state.addons };
    if (quantity === null) delete next[id];
    else next[id] = quantity;
    dispatch({ type: 'addons', addons: next });
  };

  const save = useMutation({
    mutationFn: () => api.updateDraft(state.draftId!, draftBody(state, 'Summary')!),
    onSuccess: onContinued,
    onError: () => dispatch({ type: 'jumpCancelled' }),
  });

  // A later progress step was clicked: run this step's Continue once the capacity figures are in (decision 41).
  useEffect(() => {
    if (state.jumpTarget && isAfter(state.jumpTarget, 'addons') && !capacityLoading && !save.isPending) save.mutate();
  }, [state.jumpTarget, capacityLoading]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <StepLayout
      title="Add-ons"
      bar
      barContent={
        <>
          <div className={small ? styles.totalInBar : styles.total} data-testid="booking-price">
            <div>
              <div className={styles.totalLabel}>Booking price</div>
              {price.hasPriceOnRequest && <div className={styles.totalNote}>Excludes price-on-request add-ons</div>}
            </div>
            <div className={styles.totalValue}>{hire === null ? '' : formatMoney(price.total)}</div>
          </div>
          {/* In the bar, a thin divider separates the Booking price from Back and Continue, like the Dates & boat bar's. */}
          {small && <div className={styles.barDivider} data-testid="booking-price-divider" />}
        </>
      }
      error={save.isError ? errorMessage(save.error) : null}
      back={<Button onClick={() => dispatch({ type: 'goto', step: 'details' })} disabled={save.isPending}>Back</Button>}
      action={<Button variant="primary" onClick={() => save.mutate()} busy={save.isPending} busyLabel="Saving…" disabled={capacityLoading}>Continue</Button>}
    >
      {state.notice && <Notice tone="error">{state.notice}</Notice>}
      {removedNotice && <Notice tone="warning">{removedNotice}</Notice>}
      <Notice>
        Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance. Add-on prices are subject to change during
        peak periods.
      </Notice>
      {boat.isLoading && <p>Loading add-ons…</p>}
      {!boat.isLoading && visible.length === 0 && <p>There are no add-ons for this boat.</p>}
      <ul className={styles.list}>
        {visible.map((a) => {
          const ticked = state.addons[a.id] !== undefined;
          const p = addonPrice(a, periodTypeOfState(state)!);
          const left = remainingFor(a.id);
          const max = left ?? 99;
          return (
            <li key={a.id} className={`${styles.item} ${ticked ? styles.ticked : ''}`}>
              <label className={styles.check}>
                <input type="checkbox" checked={ticked} disabled={save.isPending} onChange={(e) => setQuantity(a.id, e.target.checked ? 1 : null)} />
                <span className={styles.text}>
                  <span className={styles.name}>{a.name}</span>
                  {a.description && <span className={styles.description}>{a.description}</span>}
                  {a.hasCapacity && left !== undefined && (
                    <span className={styles.remaining}>{left} {a.capacityUnitLabel} remaining</span>
                  )}
                </span>
              </label>
              <div className={styles.side}>
                <span className={styles.price}>{p === null ? 'Price on request' : `${formatMoney(p)} ${a.quantityApplies ? 'each' : 'per booking'}`}</span>
                {a.quantityApplies && ticked && (
                  <label className={styles.qty}>
                    <span className="visually-hidden">Quantity of {a.name}</span>
                    <input
                      type="number"
                      min={1}
                      max={max}
                      value={state.addons[a.id]}
                      disabled={save.isPending}
                      onChange={(e) => {
                        const n = Math.floor(Number(e.target.value));
                        setQuantity(a.id, Math.min(Math.max(Number.isFinite(n) ? n : 1, 1), max));
                      }}
                    />
                  </label>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </StepLayout>
  );
}
