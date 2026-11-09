import type { ReactNode } from 'react';
import styles from './StepLayout.module.css';

/** A step's heading, content and action row (Back on the left, the main action on the right). */
export function StepLayout({ title, children, back, action, error }: {
  title: string;
  children: ReactNode;
  back?: ReactNode;
  action?: ReactNode;
  error?: string | null;
}) {
  return (
    <section className={styles.step} aria-label={title}>
      <h2 className={styles.title}>{title}</h2>
      {children}
      {error && <div className={styles.error} role="alert">{error}</div>}
      {(back || action) && (
        <div className={styles.actions}>
          <div>{back}</div>
          <div>{action}</div>
        </div>
      )}
    </section>
  );
}
