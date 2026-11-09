import type { ReactNode } from 'react';
import { NavLink } from 'react-router';
import styles from './Layout.module.css';

const LINKS = [
  { to: '/bookings', label: 'Bookings' },
  { to: '/fleet', label: 'Fleet' },
  { to: '/customers', label: 'Customers' },
  { to: '/settings', label: 'Settings' },
];

export function Layout({ children }: { children: ReactNode }) {
  return (
    <div className={styles.app}>
      <header className={styles.bar}>
        <div className={styles.brand}>Berger Houseboats</div>
        <nav aria-label="Main">
          <ul className={styles.nav}>
            {LINKS.map((l) => (
              <li key={l.to}>
                <NavLink to={l.to} className={({ isActive }) => `${styles.link} ${isActive ? styles.active : ''}`}>
                  {l.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </header>
      <main className={styles.main}>{children}</main>
    </div>
  );
}

export function PageHeader({ title, actions }: { title: string; actions?: ReactNode }) {
  return (
    <div className={styles.pageHeader}>
      <h1 className={styles.pageTitle}>{title}</h1>
      {actions && <div className={styles.pageActions}>{actions}</div>}
    </div>
  );
}
