import type { KeyboardEvent } from 'react';
import { formatMoney } from '@shared/utils/money';
import { BoatImage } from '@/components/BoatImage/BoatImage';
import type { CardBoat } from '../utils/availability';
import styles from './BoatCards.module.css';

export interface BoatCardsProps {
  requested: CardBoat | null;
  /** The requested boat's grey reason, or null when it can be selected. */
  requestedReason: string | null;
  others: CardBoat[];
  emptyMessage: string | null;
  selectedSlug: string | null;
  prices: Record<string, number | null | undefined>;
  /** A card was clicked: it selects that boat, or deselects it when it was the selected one. */
  onToggle: (slug: string) => void;
}

/**
 * The boat cards: toggle buttons, at most one pressed (§11.2). Clicking the selected card again deselects it. The requested
 * boat is always first.
 */
export function BoatCards({ requested, requestedReason, others, emptyMessage, selectedSlug, prices, onToggle }: BoatCardsProps) {
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>, slug: string) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onToggle(slug);
    }
  };

  const card = (boat: CardBoat, reason: string | null) => {
    const selected = boat.slug === selectedSlug;
    const price = prices[boat.slug];
    return (
      <div
        key={boat.slug}
        data-slug={boat.slug}
        data-testid={`boat-${boat.slug}`}
        role="button"
        aria-pressed={selected}
        aria-disabled={reason ? true : undefined}
        aria-label={boat.name}
        aria-describedby={reason ? `${boat.slug}-reason` : undefined}
        tabIndex={reason ? -1 : 0}
        className={[styles.card, selected ? styles.selected : '', reason ? styles.greyed : ''].join(' ')}
        onClick={() => !reason && onToggle(boat.slug)}
        onKeyDown={(e) => !reason && onKeyDown(e, boat.slug)}
      >
        <BoatImage src={boat.imageUrl} alt="" className={styles.image} />
        <div className={styles.info}>
          <div className={styles.name}>{boat.name}</div>
          <div className={styles.berth}>{boat.berth} berth</div>
          {(boat.hasSpa || boat.isPetFriendly) && (
            <div className={styles.badges}>
              {boat.hasSpa && <span className={styles.badge}>Spa</span>}
              {boat.isPetFriendly && <span className={styles.badge}>Pet friendly</span>}
            </div>
          )}
          {price !== undefined && price !== null && !reason && <div className={styles.price}>{formatMoney(price)}</div>}
          {reason && <div className={styles.reason} id={`${boat.slug}-reason`}>{reason}</div>}
        </div>
      </div>
    );
  };

  return (
    <div className={styles.cards} role="group" aria-label="Boats">
      {requested && card(requested, requestedReason)}
      {requested && <div className={styles.separator}><span>Other boats available</span></div>}
      {others.map((b) => card(b, null))}
      {emptyMessage && <p className={styles.empty}>{emptyMessage}</p>}
    </div>
  );
}
