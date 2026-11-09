import type { ReactNode } from 'react';
import styles from './Section.module.css';

/** A white settings-style card with a heading and optional help text. */
export function Section({ id, title, help, children }: { id?: string; title: string; help?: ReactNode; children: ReactNode }) {
  return (
    <section id={id} className={styles.section} aria-labelledby={id ? `${id}-title` : undefined}>
      <h2 id={id ? `${id}-title` : undefined} className={styles.title}>{title}</h2>
      {help && <div className={styles.help}>{help}</div>}
      {children}
    </section>
  );
}
