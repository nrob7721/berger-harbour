import { isAfter, type Step } from '@/lib/booking-state';
import styles from './StepProgress.module.css';

const STEPS: { step: Step; label: string }[] = [
  { step: 'dates', label: 'Dates & boat' },
  { step: 'details', label: 'Your details' },
  { step: 'addons', label: 'Add-ons' },
  { step: 'summary', label: 'Summary' },
  { step: 'payment', label: 'Payment' },
];

/** Payment is only entered with the Summary's Pay button (the terms are ticked there), and Complete is the end. */
const NEVER_CLICKABLE: readonly Step[] = ['payment', 'complete'];

/**
 * The progress steps (decision 41): each step up to the furthest reached is a button, except the current step, Payment and
 * Complete. Clicking one goes back to it, or forward through each step's Continue.
 */
export function StepProgress({ current, furthest, busy, onSelect }: {
  current: Step;
  furthest: Step;
  /** A jump is running: the steps wait for it. */
  busy: boolean;
  onSelect: (step: Step) => void;
}) {
  const index = current === 'complete' ? STEPS.length : STEPS.findIndex((s) => s.step === current);
  return (
    <ol className={styles.progress} aria-label="Booking progress" aria-busy={busy || undefined}>
      {STEPS.map((s, i) => {
        const clickable = s.step !== current && current !== 'complete' && !NEVER_CLICKABLE.includes(s.step) && !isAfter(s.step, furthest);
        const label = `${i + 1}. ${s.label}`;
        return (
          <li
            key={s.step}
            className={[styles.item, i === index ? styles.current : '', i < index ? styles.done : '', clickable ? styles.clickable : ''].join(' ')}
            aria-current={i === index ? 'step' : undefined}
          >
            {clickable ? (
              <button type="button" className={styles.button} onClick={() => onSelect(s.step)} disabled={busy}>
                <span className={styles.label}>{label}</span>
              </button>
            ) : (
              <span className={styles.label}>{label}</span>
            )}
          </li>
        );
      })}
    </ol>
  );
}
