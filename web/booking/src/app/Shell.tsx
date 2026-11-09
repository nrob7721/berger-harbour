import type { ReactNode } from 'react';
import styles from './Shell.module.css';

/** The page frame: brand eyebrow, title, optional progress, and a white card. */
export function Shell({ title, progress, children }: { title: string; progress?: ReactNode; children: ReactNode }) {
  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div className={styles.brand}>Berger Houseboats</div>
        <h1 className={styles.title}>{title}</h1>
        {progress}
      </header>
      <main className={styles.card}>{children}</main>
    </div>
  );
}
