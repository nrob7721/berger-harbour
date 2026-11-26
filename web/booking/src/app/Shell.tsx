import type { ReactNode } from 'react';
import styles from './Shell.module.css';

/**
 * The page frame: brand eyebrow, optional title and progress steps, and a white card. The booking app passes no title: each
 * step's own heading is the only heading (§11.2). The pay page has one. `fill` makes the frame exactly the viewport's height, so
 * the page itself never scrolls and the step scrolls inside it (the desktop Dates & boat step; ignored on a short viewport).
 */
export function Shell({ title, progress, fill, children }: { title?: string; progress?: ReactNode; fill?: boolean; children: ReactNode }) {
  return (
    <div className={`${styles.page} ${fill ? styles.fill : ''}`}>
      <header className={styles.header}>
        <div className={styles.brand}>Berger Houseboats</div>
        {title && <h1 className={styles.title}>{title}</h1>}
        {progress}
      </header>
      <main className={styles.card}>{children}</main>
    </div>
  );
}
