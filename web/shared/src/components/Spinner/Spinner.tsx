import styles from './Spinner.module.css';

export type SpinnerSize = 'small' | 'large';

/** A small spinner for buttons and inline text, or a large one for an area that is loading. */
export function Spinner({ label, size = 'small' }: { label?: string; size?: SpinnerSize }) {
  return (
    <span className={`${styles.spinner} ${styles[size]}`} role={label ? 'status' : undefined} aria-label={label}>
      <span className="visually-hidden">{label}</span>
    </span>
  );
}
