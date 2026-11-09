import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { CountryCode } from 'libphonenumber-js/max';
import { countries, splitNumber, toE164 } from '../../utils/phone';
import styles from './MobileInput.module.css';

export interface MobileInputProps {
  id?: string;
  /** E.164 when valid, otherwise what the user typed (with the calling code), so validation can explain the problem. */
  value: string | null | undefined;
  onChange: (value: string) => void;
  onBlur?: () => void;
  disabled?: boolean;
  invalid?: boolean;
  'aria-describedby'?: string;
}

/** A mobile number with a searchable country code selector, defaulting to Australia (+61). */
export function MobileInput({ id, value, onChange, onBlur, disabled, invalid, ...aria }: MobileInputProps) {
  const initial = useMemo(() => splitNumber(value), []); // eslint-disable-line react-hooks/exhaustive-deps
  const [country, setCountry] = useState<CountryCode>(initial.country);
  const [national, setNational] = useState(initial.national);
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const listId = useId();
  const wrapper = useRef<HTMLDivElement>(null);
  const lastEmitted = useRef(value ?? '');

  // Follow outside changes (e.g. a customer lookup pre-filling the number).
  useEffect(() => {
    if ((value ?? '') === lastEmitted.current) return;
    const s = splitNumber(value);
    setCountry(s.country);
    setNational(s.national);
    lastEmitted.current = value ?? '';
  }, [value]);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (!wrapper.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const all = countries();
  const selected = all.find((c) => c.code === country) ?? all[0];
  const filtered = search
    ? all.filter((c) => c.name.toLowerCase().includes(search.toLowerCase()) || `+${c.callingCode}`.includes(search.replace(/\s/g, '')))
    : all;

  const emit = (nextCountry: CountryCode, nextNational: string) => {
    const digits = nextNational.trim();
    const out = digits === '' ? '' : (toE164(digits, nextCountry) ?? `+${all.find((c) => c.code === nextCountry)?.callingCode ?? ''} ${digits}`);
    lastEmitted.current = out;
    onChange(out);
  };

  return (
    <div ref={wrapper} className={`${styles.wrapper} ${invalid ? styles.invalid : ''}`}>
      <button
        type="button"
        className={styles.country}
        onClick={() => setOpen((o) => !o)}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={`Country code: ${selected.name} +${selected.callingCode}`}
      >
        {selected.code} +{selected.callingCode} <span aria-hidden="true">▾</span>
      </button>
      <input
        id={id}
        className={styles.number}
        type="tel"
        inputMode="tel"
        autoComplete="tel-national"
        value={national}
        disabled={disabled}
        aria-invalid={invalid || undefined}
        aria-describedby={aria['aria-describedby']}
        onChange={(e) => {
          setNational(e.target.value);
          emit(country, e.target.value);
        }}
        onBlur={onBlur}
      />
      {open && (
        <div className={styles.popover}>
          <input
            className={styles.search}
            placeholder="Search countries"
            aria-label="Search countries"
            value={search}
            autoFocus
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') {
                e.stopPropagation();
                setOpen(false);
              }
            }}
          />
          <ul className={styles.list} role="listbox" id={listId} aria-label="Countries">
            {filtered.map((c) => (
              <li key={c.code}>
                <button
                  type="button"
                  role="option"
                  aria-selected={c.code === country}
                  className={styles.option}
                  onClick={() => {
                    setCountry(c.code);
                    setOpen(false);
                    setSearch('');
                    emit(c.code, national);
                  }}
                >
                  <span>{c.name}</span>
                  <span className={styles.code}>+{c.callingCode}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
