import { useRef, type KeyboardEvent } from 'react';
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
  onSelect: (slug: string) => void;
}

/** A single-select radio group of boat cards. The requested boat is always first. */
export function BoatCards({ requested, requestedReason, others, emptyMessage, selectedSlug, prices, onSelect }: BoatCardsProps) {
  const group = useRef<HTMLDivElement>(null);
  const cards = [...(requested ? [{ boat: requested, reason: requestedReason }] : []), ...others.map((boat) => ({ boat, reason: null }))];
  const enabled = cards.filter((c) => !c.reason).map((c) => c.boat.slug);
  const focusable = selectedSlug && enabled.includes(selectedSlug) ? selectedSlug : enabled[0];

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>, slug: string) => {
    const delta = e.key === 'ArrowDown' || e.key === 'ArrowRight' ? 1 : e.key === 'ArrowUp' || e.key === 'ArrowLeft' ? -1 : 0;
    if (delta) {
      e.preventDefault();
      const i = enabled.indexOf(slug);
      const next = enabled[(i + delta + enabled.length) % enabled.length];
      group.current?.querySelector<HTMLElement>(`[data-slug="${next}"]`)?.focus();
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onSelect(slug);
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
        role="radio"
        aria-checked={selected}
        aria-disabled={reason ? true : undefined}
        tabIndex={!reason && boat.slug === focusable ? 0 : -1}
        className={[styles.card, selected ? styles.selected : '', reason ? styles.greyed : ''].join(' ')}
        onClick={() => !reason && onSelect(boat.slug)}
        onKeyDown={(e) => !reason && onKeyDown(e, boat.slug)}
      >
        <BoatImage src={boat.imageUrl} alt={boat.name} className={styles.image} />
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
          {reason && <div className={styles.reason}>{reason}</div>}
        </div>
        <span className={styles.tick} aria-hidden="true">
          {selected ? '✓' : ''}
        </span>
      </div>
    );
  };

  return (
    <div ref={group} className={styles.cards} role="radiogroup" aria-label="Boats">
      {requested && card(requested, requestedReason)}
      {requested && <div className={styles.separator}><span>Other boats available</span></div>}
      {others.map((b) => card(b, null))}
      {emptyMessage && <p className={styles.empty}>{emptyMessage}</p>}
    </div>
  );
}
