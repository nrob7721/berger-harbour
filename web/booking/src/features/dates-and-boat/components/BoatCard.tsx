import { useId, useState, type MouseEvent } from 'react';
import { formatMoney } from '@shared/utils/money';
import { BoatImage } from '@/components/BoatImage/BoatImage';
import type { CardBoat } from '../utils/availability';
import { BoatDetails, type MoreDetailsState } from './BoatDetails';
import styles from './BoatCard.module.css';

/** `compact`: the Booking details view. `detailed`: the Boats view and the small-screen boat expander (§11.2). */
export type BoatCardLayout = 'compact' | 'detailed';

/** Tags shown on a small screen before "+{n} more". */
export const SMALL_SCREEN_TAGS = 5;

export interface BoatCardProps {
  boat: CardBoat;
  layout: BoatCardLayout;
  /** On a small screen only the first 5 tags show at first. */
  small: boolean;
  selected: boolean;
  /** The grey reason a requested boat can't be selected, or null. */
  reason: string | null;
  price: number | null | undefined;
  detailsOpen: boolean;
  details: MoreDetailsState;
  onToggle: () => void;
  onToggleDetails: () => void;
}

/** The card's own controls: a click on one never toggles the selection. */
const CONTROL = '[data-card-control]';

/**
 * One boat card (§11.2): a container that toggles the boat's selection when clicked anywhere outside its own controls, with an
 * inner toggle button (`aria-pressed`) carrying the selection for keyboard and screen readers. A detailed card shows the boat's
 * tags and a More details button; opening the details never selects the boat, and selecting never opens them.
 */
export function BoatCard({ boat, layout, small, selected, reason, price, detailsOpen, details, onToggle, onToggleDetails }: BoatCardProps) {
  const detailsId = useId();
  const [allTags, setAllTags] = useState(false);
  const detailed = layout === 'detailed';
  const open = detailed && detailsOpen;

  const onClick = (e: MouseEvent<HTMLDivElement>) => {
    if (reason || (e.target as HTMLElement).closest(CONTROL)) return;
    onToggle();
  };

  const tags = detailed ? boat.tags : [];
  const shownTags = small && !allTags ? tags.slice(0, SMALL_SCREEN_TAGS) : tags;
  const hiddenTags = tags.length - shownTags.length;

  return (
    <div
      data-slug={boat.slug}
      data-testid={`boat-${boat.slug}`}
      data-boat-card
      className={[
        styles.card, styles[layout], open ? styles.open : '', selected ? styles.selected : '', reason ? styles.greyed : '',
      ].join(' ')}
      onClick={onClick}
    >
      {open ? (
        <BoatDetails id={detailsId} boatName={boat.name} state={details} mediaClassName={styles.media} bodyClassName={styles.details} />
      ) : (
        <div className={styles.media}>
          <BoatImage src={boat.thumbnailUrl} alt="" className={styles.image} />
        </div>
      )}
      <div className={styles.info}>
        <button
          type="button"
          className={styles.summary}
          aria-pressed={selected}
          aria-label={boat.name}
          aria-disabled={reason ? true : undefined}
          aria-describedby={reason ? `${boat.slug}-reason` : undefined}
          tabIndex={reason ? -1 : 0}
        >
          <span className={styles.name}>{boat.name}</span>
          <span className={styles.berth}>{boat.berth} berth</span>
          {(boat.hasSpa || boat.isPetFriendly) && (
            <span className={styles.badges}>
              {boat.hasSpa && <span className={styles.badge}>Spa</span>}
              {boat.isPetFriendly && <span className={styles.badge}>Pet friendly</span>}
            </span>
          )}
          {price !== undefined && price !== null && !reason && <span className={styles.price}>{formatMoney(price)}</span>}
        </button>
        {reason && <div className={styles.reason} id={`${boat.slug}-reason`}>{reason}</div>}
        {shownTags.length > 0 && (
          <ul className={styles.tags} aria-label={`${boat.name} features`}>
            {shownTags.map((t) => <li key={t} className={styles.tag}>{t}</li>)}
            {hiddenTags > 0 && (
              <li>
                <button type="button" className={`${styles.tag} ${styles.moreTags}`} data-card-control onClick={() => setAllTags(true)}>
                  +{hiddenTags} more
                </button>
              </li>
            )}
          </ul>
        )}
      </div>
      {detailed && (
        <div className={styles.more}>
          <button
            type="button"
            className={styles.moreButton}
            data-card-control
            aria-expanded={open}
            aria-controls={open ? (details.status === 'ready' ? `${detailsId}-media ${detailsId}-body` : `${detailsId}-media`) : undefined}
            onClick={onToggleDetails}
          >
            {open ? 'Hide details' : 'More details'}
          </button>
        </div>
      )}
    </div>
  );
}
