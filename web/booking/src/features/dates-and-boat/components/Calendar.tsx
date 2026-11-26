import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { addDays, dayOfMonth, formatDateWithWeekday, isSameMonth, monthGrid, monthTitle, type IsoDate } from '@shared/utils/dates';
import type { Stay, TypeOfStay } from '@shared/utils/periods';
import { useSwipe } from '@/hooks/useSwipe';
import type { BookingSettings, FleetAvailability } from '@/lib/types';
import { dayState, selectionMark, type DayState } from '../utils/availability';
import styles from './Calendar.module.css';

export interface CalendarProps {
  month: IsoDate;
  canGoBack: boolean;
  canGoForward: boolean;
  onMonthChange: (delta: 1 | -1) => void;
  /** Null before the customer chooses one: the calendar is then covered by an inert overlay (decision 44). */
  typeOfStay: TypeOfStay | null;
  period: Stay | null;
  availability: FleetAvailability | undefined;
  loading: boolean;
  boats: string[];
  settings: BookingSettings;
  onSelect: (period: Stay) => void;
  onEnquire: (period: Stay) => void;
}

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/** Before a type of stay is chosen, the month's days carry no availability styling. */
type CellState = DayState | { kind: 'neutral' };

/**
 * A classic Monday-first month calendar. Arrow keys move between days, Enter or Space selects, and on a touch screen a horizontal
 * swipe changes the month like Prev/Next (§11.2). Until a type of stay is chosen it is covered by an inert overlay.
 */
export function Calendar(props: CalendarProps) {
  const { month, period, typeOfStay, availability, boats, settings, loading } = props;
  const grid = monthGrid(month);
  const states = new Map<IsoDate, CellState>();
  for (const week of grid)
    for (const day of week) {
      const inMonth = isSameMonth(day, month);
      states.set(day, typeOfStay || !inMonth ? dayState(day, inMonth, typeOfStay ?? 'Midweek', availability, boats, settings) : { kind: 'neutral' });
    }
  const swipe = useSwipe((d) => {
    if (d === 'next' && props.canGoForward) props.onMonthChange(1);
    if (d === 'previous' && props.canGoBack) props.onMonthChange(-1);
  }, !!typeOfStay);

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
    if (!typeOfStay) return; // the inert overlay is up (decision 44)
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
    } else if ((e.key === 'Enter' || e.key === ' ') && typeOfStay) {
      e.preventDefault();
      activate(day);
    }
  };

  return (
    <div className={styles.wrap}>
      <div className={styles.calendar} inert={!typeOfStay} data-testid="calendar" {...swipe}>
        <div className={styles.header}>
          <button type="button" className={styles.nav} onClick={() => props.onMonthChange(-1)} disabled={!typeOfStay || !props.canGoBack} aria-label="Previous month">
            ‹
          </button>
          <h3 className={styles.title} aria-live="polite">
            {monthTitle(month)}
          </h3>
          <button type="button" className={styles.nav} onClick={() => props.onMonthChange(1)} disabled={!typeOfStay || !props.canGoForward} aria-label="Next month">
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
                const neutral = state.kind === 'neutral';
                const disabled = state.kind === 'disabled';
                const enquire = state.kind === 'enquire' || (state.kind === 'disabled' && state.enquire);
                const label = [
                  formatDateWithWeekday(day),
                  mark === 'in' ? 'check-in' : mark === 'out' ? 'check-out' : mark === 'inner' ? 'selected' : '',
                  neutral ? '' : disabled ? 'unavailable' : enquire ? 'phone or email to book' : '',
                ].filter(Boolean).join(', ');
                return (
                  <div key={day} role="gridcell">
                    <button
                      type="button"
                      data-day={day}
                      data-testid={`day-${day}`}
                      tabIndex={typeOfStay && day === focusDay ? 0 : -1}
                      aria-disabled={disabled || neutral || undefined}
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
      {!typeOfStay && (
        <div className={styles.overlay} data-testid="calendar-overlay">
          <p className={styles.overlayText}>Select a type of stay to view availability</p>
        </div>
      )}
    </div>
  );
}
