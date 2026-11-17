import type { ReactNode } from 'react';
import styles from './Shell.module.css';

/**
 * The page frame: brand eyebrow, optional title and progress steps, and a white card. The booking app passes no title: each
 * step's own heading is the only heading (§11.2). The pay page has one.
 */
export function Shell({ title, progress, children }: { title?: string; progress?: ReactNode; children: ReactNode }) {
  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.brand}>Berger Houseboats</div>
        {title && <h1 className={styles.title}>{title}</h1>}
        {progress}
      </header>
      <main className={styles.card}>{children}</main>
    </div>
  );
}
