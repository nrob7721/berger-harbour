import type { Step } from '@/lib/booking-state';
import styles from './StepProgress.module.css';

const STEPS: { step: Step; label: string }[] = [
  { step: 'dates', label: 'Dates & boat' },
  { step: 'details', label: 'Your details' },
  { step: 'addons', label: 'Add-ons' },
  { step: 'summary', label: 'Summary' },
  { step: 'payment', label: 'Payment' },
];

export function StepProgress({ current }: { current: Step }) {
  const index = current === 'complete' ? STEPS.length : STEPS.findIndex((s) => s.step === current);
  return (
    <ol className={styles.progress} aria-label="Booking progress">
      {STEPS.map((s, i) => (
        <li
          key={s.step}
          className={[styles.item, i === index ? styles.current : '', i < index ? styles.done : ''].join(' ')}
          aria-current={i === index ? 'step' : undefined}
        >
          <span className={styles.label}>{i + 1}. {s.label}</span>
        </li>
      ))}
    </ol>
  );
}
