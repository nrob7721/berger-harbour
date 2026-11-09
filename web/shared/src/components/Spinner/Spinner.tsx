import styles from './Spinner.module.css';

export function Spinner({ label, size = 16 }: { label?: string; size?: number }) {
  return (
    <span className={styles.spinner} style={{ width: size, height: size }} role={label ? 'status' : undefined} aria-label={label}>
      <span className="visually-hidden">{label}</span>
    </span>
  );
}
