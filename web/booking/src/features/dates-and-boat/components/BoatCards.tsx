import type { CardBoat } from '../utils/availability';
import { BoatCard, type BoatCardLayout } from './BoatCard';
import type { MoreDetailsState } from './BoatDetails';
import styles from './BoatCards.module.css';

export interface BoatCardsProps {
  /** `compact` in the Booking details view; `detailed` in the Boats view and the small-screen expander (§6.1). */
  layout: BoatCardLayout;
  requested: CardBoat | null;
  /** The requested boat's grey reason, or null when it can be selected. */
  requestedReason: string | null;
  others: CardBoat[];
  emptyMessage: string | null;
  selectedSlug: string | null;
  prices: Record<string, number | null | undefined>;
  /** The boats whose More details are open. */
  openDetails: ReadonlySet<string>;
  detailsFor: (slug: string) => MoreDetailsState;
  /** A card was clicked: it selects that boat, or deselects it when it was the selected one. */
  onToggle: (slug: string) => void;
  onToggleDetails: (slug: string) => void;
}

/**
 * The boat cards (§11.2): at most one is selected, and clicking the selected card again deselects it. The requested boat is
 * always first, then "Other boats available".
 */
export function BoatCards(props: BoatCardsProps) {
  const { requested, requestedReason, others, emptyMessage, selectedSlug, prices } = props;
  const card = (boat: CardBoat, reason: string | null) => (
    <BoatCard
      key={boat.slug}
      boat={boat}
      layout={props.layout}
      selected={boat.slug === selectedSlug}
      reason={reason}
      price={prices[boat.slug]}
      detailsOpen={props.openDetails.has(boat.slug)}
      details={props.detailsFor(boat.slug)}
      onToggle={() => props.onToggle(boat.slug)}
      onToggleDetails={() => props.onToggleDetails(boat.slug)}
    />
  );

  return (
    <div className={styles.cards} role="group" aria-label="Boats">
      {requested && card(requested, requestedReason)}
      {requested && <div className={styles.separator}><span>Other boats available</span></div>}
      {others.map((b) => card(b, null))}
      {emptyMessage && <p className={styles.empty}>{emptyMessage}</p>}
    </div>
  );
}
