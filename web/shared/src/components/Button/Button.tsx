import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Spinner } from '../Spinner/Spinner';
import styles from './Button.module.css';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost' | 'link';
  size?: 'sm' | 'md';
  /**
   * A square button showing only an icon; it needs an `aria-label`. While busy, the spinner replaces the icon and the
   * accessible label is the progress label.
   */
  iconOnly?: boolean;
  /** While a backend request runs: disabled, with a spinner and a progress label ("Saving…"). */
  busy?: boolean;
  busyLabel?: string;
  children: ReactNode;
}

export function Button({
  variant = 'secondary', size = 'md', iconOnly = false, busy = false, busyLabel, disabled, children, className, type = 'button', ...rest
}: ButtonProps) {
  let content: ReactNode = children;
  if (busy) {
    content = iconOnly ? <Spinner size="small" /> : (
      <>
        <Spinner size="small" />
        <span>{busyLabel ?? children}</span>
      </>
    );
  }
  return (
    <button
      type={type}
      className={[styles.button, styles[variant], styles[size], iconOnly && styles.iconOnly, className].filter(Boolean).join(' ')}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      {...rest}
      aria-label={iconOnly && busy && busyLabel ? busyLabel : rest['aria-label']}
    >
      {content}
    </button>
  );
}
