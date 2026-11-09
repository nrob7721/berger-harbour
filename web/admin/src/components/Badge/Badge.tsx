import type { ReactNode } from 'react';
import styles from './Badge.module.css';

export function Badge({ tone = 'info', children, title }: { tone?: 'info' | 'success' | 'muted' | 'warning' | 'danger'; children: ReactNode; title?: string }) {
  return <span className={`${styles.badge} ${styles[tone]}`} title={title}>{children}</span>;
}
