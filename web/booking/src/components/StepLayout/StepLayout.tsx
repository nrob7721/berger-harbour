import type { ReactNode } from 'react';
import { StepActionBar } from '@/components/StepActionBar/StepActionBar';
import { useSmallScreen } from '@/hooks/useSmallScreen';
import styles from './StepLayout.module.css';

/**
 * A step's heading, content and action row (Back on the left, the main action on the right). A step that opts in to the `bar`
 * shows the action row, its error and `barContent` in the step action bar on a small screen (§11.2); otherwise `barContent`
 * is the last thing in the step.
 */
export function StepLayout({ title, children, back, action, error, bar = false, barContent }: {
  title: string;
  children: ReactNode;
  back?: ReactNode;
  action?: ReactNode;
  error?: string | null;
  bar?: boolean;
  barContent?: ReactNode;
}) {
  const inBar = useSmallScreen() && bar;
  return (
    <section className={styles.step} aria-label={title}>
      <h2 className={styles.title}>{title}</h2>
      {children}
      {inBar ? (
        <StepActionBar start={back} end={action} error={error}>{barContent}</StepActionBar>
      ) : (
        <>
          {barContent}
          {error && <div className={styles.error} role="alert">{error}</div>}
          {(back || action) && (
            <div className={styles.actions}>
              <div>{back}</div>
              <div>{action}</div>
            </div>
          )}
        </>
      )}
    </section>
  );
}
