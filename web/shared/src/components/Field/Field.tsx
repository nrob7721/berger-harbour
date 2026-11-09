import type { ReactNode } from 'react';
import styles from './Field.module.css';

export interface FieldProps {
  label: ReactNode;
  htmlFor?: string;
  error?: string;
  hint?: ReactNode;
  children: ReactNode;
  className?: string;
}

/** A labelled form control with optional hint and error text. */
export function Field({ label, htmlFor, error, hint, children, className }: FieldProps) {
  return (
    <div className={[styles.field, className].filter(Boolean).join(' ')}>
      <label className={styles.label} htmlFor={htmlFor}>
        {label}
      </label>
      {children}
      {hint && !error && <div className={styles.hint}>{hint}</div>}
      {error && (
        <div className={styles.error} role="alert" id={htmlFor ? `${htmlFor}-error` : undefined}>
          {error}
        </div>
      )}
    </div>
  );
}

export const inputClass = styles.input;
