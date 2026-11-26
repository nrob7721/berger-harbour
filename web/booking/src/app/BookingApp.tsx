import { useEffect, useState } from 'react';
import { ApiError } from '@shared/lib/http';
import { Notice } from '@/components/Notice/Notice';
import { StepProgress } from '@/components/StepProgress/StepProgress';
import { AddonsStep } from '@/features/addons/components/AddonsStep';
import { CompleteStep } from '@/features/complete/components/CompleteStep';
import { DatesAndBoatStep } from '@/features/dates-and-boat/components/DatesAndBoatStep';
import { PayButton, PaymentStep } from '@/features/deposit-checkout/components/DepositCheckout';
import { DetailsStep } from '@/features/details/components/DetailsStep';
import { SummaryStep } from '@/features/summary/components/SummaryStep';
import { useBoatDetails, useFleet, useFleetDetails } from '@/hooks/useFleet';
import { useSmallScreen } from '@/hooks/useSmallScreen';
import { BookingProvider, initialState, useBooking, type BookingState } from '@/lib/booking-state';
import { resumeProgress } from '@/lib/resume-progress';
import { clearSavedProgress, readSavedProgress } from '@/lib/saved-progress';
import type { BoatDetails, BoatsResponse } from '@/lib/types';
import { Shell } from './Shell';
import { useAdvance } from './useAdvance';
import styles from './BookingApp.module.css';

export interface BookingAppProps {
  requestedSlug: string | null;
  /** Coming back from a Stripe redirect: ?booking=&session_id=. */
  returnedBooking: { bookingId: string; sessionId: string } | null;
}

/** Loads the fleet (and the requested boat) while a skeleton shows, then runs the steps. */
export function BookingApp({ requestedSlug, returnedBooking }: BookingAppProps) {
  const fleet = useFleet();
  const requested = useBoatDetails(requestedSlug);

  if (fleet.isLoading || (requestedSlug && requested.isLoading)) return <Skeleton />;
  if (requestedSlug && requested.error instanceof ApiError && requested.error.status === 404) {
    return (
      <Shell>
        <Notice tone="error">Sorry, we couldn't find that boat. Please go back to our website and choose a boat to book.</Notice>
      </Shell>
    );
  }
  if (fleet.isError || !fleet.data) {
    return (
      <Shell>
        <Notice tone="error">We couldn't load the booking system. Please refresh the page or try again later.</Notice>
      </Shell>
    );
  }
  return <Flow fleet={fleet.data} requested={requested.data ?? null} requestedSlug={requestedSlug} returnedBooking={returnedBooking} />;
}

function Flow({ fleet, requested, requestedSlug, returnedBooking }: {
  fleet: BoatsResponse;
  requested: BoatDetails | null;
  requestedSlug: string | null;
  returnedBooking: BookingAppProps['returnedBooking'];
}) {
  const [initial, setInitial] = useState<BookingState | null>(() => (returnedBooking ? fresh() : null));
  // The boat cards' More details come in the background once the boats have loaded; nothing waits for them (§8.1).
  useFleetDetails(!returnedBooking);

  function fresh(): BookingState {
    const activeRequested = requested?.isActive ? requested.slug : null;
    return initialState(requestedSlug, {
      // An active requested boat is selected, so the calendar starts with its availability only.
      selectedSlug: activeRequested,
      ...(returnedBooking ? { step: 'complete' as const, completed: { ...returnedBooking, email: '' } } : {}),
    });
  }

  // Saved progress for the same requested boat (the same ?boat=, or none for both) is resumed; any other is discarded (§11.2).
  useEffect(() => {
    if (initial) return;
    let cancelled = false;
    const saved = readSavedProgress();
    if (!saved || saved.requestedSlug !== requestedSlug) {
      if (saved) clearSavedProgress();
      setInitial(fresh());
      return;
    }
    resumeProgress(saved, fleet.boats)
      .then((resumed) => {
        if (cancelled) return;
        if (!resumed) clearSavedProgress();
        setInitial(resumed ? initialState(requestedSlug, resumed) : fresh());
      })
      .catch(() => {
        if (!cancelled) setInitial(fresh());
      });
    return () => {
      cancelled = true;
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  if (!initial) return <Skeleton />;
  return (
    <BookingProvider initial={initial}>
      <Steps fleet={fleet} requested={requested} />
    </BookingProvider>
  );
}

function Skeleton() {
  return (
    <Shell>
      <div className={styles.skeleton} aria-busy="true" aria-label="Loading">
        <div className={styles.bar} />
        <div className={styles.block} />
        <div className={styles.bar} />
      </div>
    </Shell>
  );
}

/** The progress steps and the current step, wired together. */
export function Steps({ fleet, requested }: { fleet: BoatsResponse; requested: BoatDetails | null }) {
  const { state, dispatch } = useBooking();
  const advance = useAdvance();
  const small = useSmallScreen();
  const progress = (
    <StepProgress current={state.step} furthest={state.furthestStep} busy={state.jumpTarget !== null} onSelect={(step) => dispatch({ type: 'jump', step })} />
  );
  return (
    <Shell progress={progress} fill={state.step === 'dates' && !small}>
      {state.step === 'dates' && (
        <DatesAndBoatStep fleet={fleet.boats} settings={fleet.settings} requestedBoat={requested} onContinue={() => void advance('details')} />
      )}
      {state.step === 'details' && <DetailsStep onContinued={(changes) => void advance('addons', changes)} />}
      {state.step === 'addons' && <AddonsStep onContinued={() => void advance('summary')} />}
      {state.step === 'summary' && (
        <SummaryStep
          hireTermsUrl={fleet.settings.hireTermsUrl}
          renderPay={({ quote, termsAccepted }) => <PayButton amount={quote.amountDueAtCheckout} termsAccepted={termsAccepted} />}
        />
      )}
      {state.step === 'payment' && <PaymentStep />}
      {state.step === 'complete' && <CompleteStep />}
    </Shell>
  );
}
