import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { addDays, dayOfMonth, formatDateWithWeekday, isSameMonth, monthGrid, monthTitle, type IsoDate } from '@shared/utils/dates';
import type { OnlinePeriodType, Stay } from '@shared/utils/periods';
import type { BookingSettings, FleetAvailability } from '@/lib/types';
import { dayState, selectionMark, type DayState } from '../utils/availability';
import styles from './Calendar.module.css';

export interface CalendarProps {
  month: IsoDate;
  canGoBack: boolean;
  canGoForward: boolean;
  onMonthChange: (delta: 1 | -1) => void;
  periodType: OnlinePeriodType;
  period: Stay | null;
  availability: FleetAvailability | undefined;
  loading: boolean;
  boats: string[];
  settings: BookingSettings;
  onSelect: (period: Stay) => void;
  onEnquire: (period: Stay) => void;
}

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/** A classic Monday-first month calendar. Arrow keys move between days, Enter or Space selects. */
export function Calendar(props: CalendarProps) {
  const { month, period, periodType, availability, boats, settings, loading } = props;
  const grid = monthGrid(month);
  const states = new Map<IsoDate, DayState>();
  for (const week of grid)
    for (const day of week) states.set(day, dayState(day, isSameMonth(day, month), periodType, availability, boats, settings));

  const firstOfMonth = grid.flat().find((d) => isSameMonth(d, month))!;
  const [focusDay, setFocusDay] = useState<IsoDate>(period && isSameMonth(period.start, month) ? period.start : firstOfMonth);
  const gridRef = useRef<HTMLDivElement>(null);
  const wantFocus = useRef(false);

  useEffect(() => {
    if (!isSameMonth(focusDay, month)) setFocusDay(firstOfMonth);
  }, [month]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!wantFocus.current) return;
    wantFocus.current = false;
    gridRef.current?.querySelector<HTMLElement>(`[data-day="${focusDay}"]`)?.focus();
  }, [focusDay]);

  const activate = (day: IsoDate) => {
    const s = states.get(day);
    if (s?.kind === 'available') props.onSelect(s.period);
    else if (s?.kind === 'enquire') props.onEnquire(s.period);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, day: IsoDate) => {
    const moves: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    if (e.key in moves) {
      e.preventDefault();
      const next = addDays(day, moves[e.key]);
      if (isSameMonth(next, month)) {
        wantFocus.current = true;
        setFocusDay(next);
      }
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      activate(day);
    }
  };

  return (
    <div className={styles.calendar}>
      <div className={styles.header}>
        <button type="button" className={styles.nav} onClick={() => props.onMonthChange(-1)} disabled={!props.canGoBack} aria-label="Previous month">
          ‹
        </button>
        <h3 className={styles.title} aria-live="polite">
          {monthTitle(month)}
        </h3>
        <button type="button" className={styles.nav} onClick={() => props.onMonthChange(1)} disabled={!props.canGoForward} aria-label="Next month">
          ›
        </button>
      </div>
      <div className={styles.weekdays} aria-hidden="true">
        {WEEKDAYS.map((w) => (
          <div key={w}>{w}</div>
        ))}
      </div>
      <div ref={gridRef} className={`${styles.grid} ${loading ? styles.loading : ''}`} role="grid" aria-label={monthTitle(month)} aria-busy={loading}>
        {grid.map((week) => (
          <div key={week[0]} role="row" className={styles.row}>
            {week.map((day) => {
              const state = states.get(day)!;
              const mark = selectionMark(day, period);
              if (state.kind === 'adjacent') {
                return (
                  <div
                    key={day}
                    role="gridcell"
                    data-testid={`day-${day}`}
                    className={[styles.cell, styles.adjacent, mark ? styles.adjacentSelected : '', mark ? styles[mark] : ''].join(' ')}
                  >
                    <span className={styles.number}>{dayOfMonth(day)}</span>
                  </div>
                );
              }
              const disabled = state.kind === 'disabled';
              const enquire = state.kind === 'enquire' || (state.kind === 'disabled' && state.enquire);
              const label = [
                formatDateWithWeekday(day),
                mark === 'in' ? 'check-in' : mark === 'out' ? 'check-out' : mark === 'inner' ? 'selected' : '',
                disabled ? 'unavailable' : enquire ? 'phone or email to book' : '',
              ].filter(Boolean).join(', ');
              return (
                <div key={day} role="gridcell">
                  <button
                    type="button"
                    data-day={day}
                    data-testid={`day-${day}`}
                    tabIndex={day === focusDay ? 0 : -1}
                    aria-disabled={disabled || undefined}
                    aria-pressed={mark ? true : undefined}
                    aria-label={label}
                    className={[
                      styles.cell,
                      mark ? styles.selected : disabled ? styles.disabled : '',
                      enquire && !mark ? styles.enquire : '',
                      mark ? styles[mark] : '',
                    ].join(' ')}
                    onClick={() => activate(day)}
                    onKeyDown={(e) => onKeyDown(e, day)}
                    onFocus={() => setFocusDay(day)}
                  >
                    <span className={styles.number}>{dayOfMonth(day)}</span>
                    {mark === 'in' && <span className={styles.marker}>IN</span>}
                    {mark === 'out' && <span className={styles.marker}>OUT</span>}
                  </button>
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <ul className={styles.legend}>
        <li><span className={`${styles.swatch} ${styles.swatchAvailable}`} />Available</li>
        <li><span className={`${styles.swatch} ${styles.swatchStay}`} />Your stay</li>
        <li><span className={`${styles.swatch} ${styles.swatchUnavailable}`} />Unavailable</li>
        <li><span className={`${styles.swatch} ${styles.swatchEnquire}`} />Phone or email to book</li>
      </ul>
    </div>
  );
}
