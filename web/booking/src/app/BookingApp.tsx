import { useMemo } from 'react';
import { ApiError } from '@shared/lib/http';
import { Notice } from '@/components/Notice/Notice';
import { StepProgress } from '@/components/StepProgress/StepProgress';
import { AddonsStep } from '@/features/addons/components/AddonsStep';
import { CompleteStep } from '@/features/complete/components/CompleteStep';
import { DatesAndBoatStep } from '@/features/dates-and-boat/components/DatesAndBoatStep';
import { PayButton, PaymentStep } from '@/features/deposit-checkout/components/DepositCheckout';
import { DetailsStep } from '@/features/details/components/DetailsStep';
import { SummaryStep } from '@/features/summary/components/SummaryStep';
import { useBoatDetails, useFleet } from '@/hooks/useFleet';
import { BookingProvider, initialState, useBooking } from '@/lib/booking-state';
import type { BoatDetails, BoatsResponse } from '@/lib/types';
import { Shell } from './Shell';
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
  const title = requested.data ? `Book ${requested.data.name}` : 'Book a houseboat';

  if (fleet.isLoading || (requestedSlug && requested.isLoading)) {
    return (
      <Shell title={requestedSlug ? 'Book a houseboat' : title}>
        <div className={styles.skeleton} aria-busy="true" aria-label="Loading">
          <div className={styles.bar} />
          <div className={styles.block} />
          <div className={styles.bar} />
        </div>
      </Shell>
    );
  }
  if (requestedSlug && requested.error instanceof ApiError && requested.error.status === 404) {
    return (
      <Shell title="Book a houseboat">
        <Notice tone="error">Sorry, we couldn't find that boat. Please go back to our website and choose a boat to book.</Notice>
      </Shell>
    );
  }
  if (fleet.isError || !fleet.data) {
    return (
      <Shell title={title}>
        <Notice tone="error">We couldn't load the booking system. Please refresh the page or try again later.</Notice>
      </Shell>
    );
  }
  return <Flow fleet={fleet.data} requested={requested.data ?? null} requestedSlug={requestedSlug} returnedBooking={returnedBooking} title={title} />;
}

function Flow({ fleet, requested, requestedSlug, returnedBooking, title }: {
  fleet: BoatsResponse;
  requested: BoatDetails | null;
  requestedSlug: string | null;
  returnedBooking: BookingAppProps['returnedBooking'];
  title: string;
}) {
  const initial = useMemo(() => {
    const activeRequested = requested?.isActive ? requested.slug : null;
    return initialState(requestedSlug, {
      // An active requested boat is selected with "Show {Boat}'s availability only" ticked.
      selectedSlug: activeRequested,
      showSelectedOnly: !!activeRequested,
      ...(returnedBooking ? { step: 'complete' as const, completed: { ...returnedBooking, email: '' } } : {}),
    });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <BookingProvider initial={initial}>
      <Steps fleet={fleet} requested={requested} title={title} />
    </BookingProvider>
  );
}

function Steps({ fleet, requested, title }: { fleet: BoatsResponse; requested: BoatDetails | null; title: string }) {
  const { state, dispatch } = useBooking();
  return (
    <Shell title={title} progress={<StepProgress current={state.step} />}>
      {state.step === 'dates' && (
        <DatesAndBoatStep fleet={fleet.boats} settings={fleet.settings} requestedBoat={requested} onContinue={() => dispatch({ type: 'goto', step: 'details' })} />
      )}
      {state.step === 'details' && <DetailsStep />}
      {state.step === 'addons' && <AddonsStep />}
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
