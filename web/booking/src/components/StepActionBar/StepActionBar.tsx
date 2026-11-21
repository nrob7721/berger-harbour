import { useLayoutEffect, useRef, type ReactNode } from 'react';
import styles from './StepActionBar.module.css';

/** The CSS variable holding the bar's height: the page's bottom padding, and the boat expander's bottom edge. */
export const ACTION_BAR_HEIGHT = '--action-bar-height';

/**
 * The step action bar (§11.2): fixed to the bottom of a small screen, holding the step's buttons (`start` on the left, `end` on
 * the right), its error above them, and optionally content above both. Its measured height is published as
 * `--action-bar-height`, so nothing in the page is hidden behind it.
 */
export function StepActionBar({ children, start, end, error }: {
  children?: ReactNode;
  start?: ReactNode;
  end?: ReactNode;
  error?: string | null;
}) {
  const ref = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const root = document.documentElement;
    const publish = () => root.style.setProperty(ACTION_BAR_HEIGHT, `${el.getBoundingClientRect().height}px`);
    publish();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(publish);
    observer?.observe(el);
    return () => {
      observer?.disconnect();
      root.style.removeProperty(ACTION_BAR_HEIGHT);
    };
  }, []);

  return (
    <div ref={ref} className={styles.bar} role="region" aria-label="Step actions" data-testid="step-action-bar">
      {children}
      {error && <div className={styles.error} role="alert">{error}</div>}
      {(start || end) && (
        <div className={styles.actions}>
          <div className={styles.start}>{start}</div>
          <div className={styles.end}>{end}</div>
        </div>
      )}
    </div>
  );
}
