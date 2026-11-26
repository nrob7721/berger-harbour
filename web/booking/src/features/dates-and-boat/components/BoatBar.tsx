import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Button } from '@shared/components/Button/Button';
import { BoatImage } from '@/components/BoatImage/BoatImage';
import { StepActionBar } from '@/components/StepActionBar/StepActionBar';
import type { CardBoat } from '../utils/availability';
import styles from './BoatBar.module.css';

export interface BoatBarProps {
  /** The selected boat, or null. */
  selected: CardBoat | null;
  /** With no selected boat: the deselected boat (or greyed requested boat) and its reason, while one applies. */
  deselected: { boat: CardBoat; reason: string } | null;
  /** The Base hire rate's value: a price, or the prompt to choose dates and a boat. */
  rate: string;
  continueDisabled: boolean;
  onContinue: () => void;
  /** The boat cards; `picked` closes the expander once a card selects a boat. */
  cards: (picked: () => void) => ReactNode;
}

/**
 * Dates & boat on a small screen (§11.2): the step action bar with the selected boat row, the Base hire rate and Continue,
 * and the boat expander above it holding the boat cards.
 */
export function BoatBar({ selected, deselected, rate, continueDisabled, onContinue, cards }: BoatBarProps) {
  const [open, setOpen] = useState(false);
  const ids = { expander: useId(), heading: useId(), toggle: useId() };
  const heading = useRef<HTMLHeadingElement>(null);
  const focusToggle = () => document.getElementById(ids.toggle)?.focus();

  const close = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) focusToggle();
  };

  // Opening moves focus to the Boats heading; focus isn't trapped, so the bar stays usable. The page behind doesn't scroll.
  useEffect(() => {
    if (!open) return;
    heading.current?.focus();
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close(true);
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = overflow;
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const shown = selected ?? deselected?.boat ?? null;
  const toggleLabel = open ? 'Hide boats' : selected ? 'See other boats' : 'Choose a boat';

  return (
    <>
      <div
        id={ids.expander}
        className={`${styles.expander} ${open ? styles.open : ''}`}
        role="region"
        aria-labelledby={ids.heading}
        aria-hidden={open ? undefined : true}
        inert={!open}
        data-testid="boat-expander"
      >
        <h3 id={ids.heading} ref={heading} tabIndex={-1} className={styles.heading}>Boats</h3>
        <div className={styles.cards}>{cards(() => close(true))}</div>
      </div>

      <StepActionBar
        start={
          <div className={styles.rate} data-testid="base-hire-rate">
            <div>
              <div className={styles.rateLabel}>Base hire rate</div>
              <div className={styles.rateSub}>Excludes add-ons</div>
            </div>
            <div className={styles.rateValue}>{rate}</div>
          </div>
        }
        end={
          <Button variant="primary" disabled={continueDisabled} onClick={() => { close(false); onContinue(); }}>Continue</Button>
        }
      >
        <div className={styles.boatRow} data-testid="bar-boat">
          <BoatImage src={shown?.thumbnailUrl} alt="" className={`${styles.thumb} ${!selected && deselected ? styles.greyed : ''}`} />
          <div className={styles.boatText}>
            {selected && (
              <>
                <div className={styles.name}>{selected.name}</div>
                <div className={styles.sub}>{selected.berth} berth</div>
              </>
            )}
            {!selected && deselected && (
              <>
                <div className={`${styles.name} ${styles.greyed}`}>{deselected.boat.name}</div>
                <div className={styles.reason}>{deselected.reason}</div>
              </>
            )}
            {!selected && !deselected && <div className={styles.name}>No boat selected</div>}
          </div>
          <Button id={ids.toggle} size="sm" aria-expanded={open} aria-controls={ids.expander} onClick={() => (open ? close(true) : setOpen(true))}>
            {toggleLabel}
          </Button>
        </div>
        <div className={styles.divider} />
      </StepActionBar>
    </>
  );
}
