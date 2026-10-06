import { addDays, addMonths, dayOfWeek, daysInMonth, firstOfMonth, monthTitle, parseIso, type IsoDate } from '@shared/dates';
import { clear, h } from '@shared/dom';
import type { DayState, Stay } from '@shared/periods';

export interface CalendarOptions {
  container: HTMLElement;
  /** First and last month that can be shown (any date inside the month). */
  minMonth: IsoDate;
  maxMonth: IsoDate;
  dayState: (day: IsoDate) => DayState;
  selection: () => Stay | null;
  onActivate: (day: IsoDate, state: DayState) => void;
  onMonthChange: (month: IsoDate) => void;
}

const WEEKDAY_HEADERS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const WEEKDAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const STATUS_TEXT: Record<DayState['status'], string> = {
  'not-in-mode': 'not selectable for this type of stay',
  'too-soon': 'too soon to book online',
  'after-open-until': 'not yet open for online bookings',
  enquire: 'phone or email to book',
  taken: 'unavailable',
  available: 'available',
};

/**
 * A classic month calendar (Monday first). Keyboard: arrow keys move by day/week (crossing months), Home/End jump
 * to the start/end of the week, PageUp/PageDown change month, Enter/Space selects.
 */
export class Calendar {
  private month: IsoDate;
  private focusDay: IsoDate | null = null;
  private disabled = false;

  constructor(private readonly options: CalendarOptions) {
    this.month = firstOfMonth(options.minMonth);
  }

  get visibleMonth(): IsoDate {
    return this.month;
  }

  setDisabled(disabled: boolean): void {
    this.disabled = disabled;
    this.render();
  }

  showMonth(month: IsoDate, focus = false): void {
    const target = firstOfMonth(month);
    if (target < firstOfMonth(this.options.minMonth) || target > firstOfMonth(this.options.maxMonth)) return;
    const changed = target !== this.month;
    this.month = target;
    this.render();
    if (changed) this.options.onMonthChange(this.month);
    if (focus) this.focusCurrent();
  }

  render(): void {
    const { container } = this.options;
    const hadFocus = container.contains(document.activeElement);
    clear(container);
    const canPrev = this.month > firstOfMonth(this.options.minMonth);
    const canNext = this.month < firstOfMonth(this.options.maxMonth);
    const titleId = 'calendar-title';

    const header = h('div', { class: 'calendar__header' },
      h('button', { type: 'button', class: 'icon-button', 'aria-label': 'Previous month', disabled: !canPrev || this.disabled,
        onclick: () => this.showMonth(addMonths(this.month, -1)) }, '‹'),
      h('h3', { class: 'calendar__title', id: titleId, 'aria-live': 'polite' }, monthTitle(this.month)),
      h('button', { type: 'button', class: 'icon-button', 'aria-label': 'Next month', disabled: !canNext || this.disabled,
        onclick: () => this.showMonth(addMonths(this.month, 1)) }, '›'),
    );

    const table = h('table', { class: 'calendar__grid', role: 'grid', 'aria-labelledby': titleId });
    table.append(h('thead', {}, h('tr', {}, ...WEEKDAY_HEADERS.map((d) => h('th', { scope: 'col', abbr: d }, d)))));
    const body = h('tbody');
    const leading = (dayOfWeek(this.month) + 6) % 7; // Monday-first offset
    const total = daysInMonth(this.month);
    const selection = this.options.selection();
    const focusTarget = this.pickFocusDay(selection);
    let row = h('tr');
    for (let i = 0; i < leading; i++) row.append(h('td', { class: 'calendar__pad', role: 'presentation' }));

    for (let n = 0; n < total; n++) {
      const day = addDays(this.month, n);
      const state = this.options.dayState(day);
      row.append(h('td', { role: 'gridcell' }, this.dayButton(day, state, selection, day === focusTarget)));
      if ((leading + n + 1) % 7 === 0) {
        body.append(row);
        row = h('tr');
      }
    }
    if (row.childElementCount > 0) {
      while (row.childElementCount < 7) row.append(h('td', { class: 'calendar__pad', role: 'presentation' }));
      body.append(row);
    }
    table.append(body);
    container.append(h('div', { class: `calendar${this.disabled ? ' calendar--disabled' : ''}` }, header, table));
    if (hadFocus) this.focusCurrent();
  }

  private pickFocusDay(selection: Stay | null): IsoDate {
    const inMonth = (d: IsoDate | null | undefined): d is IsoDate => !!d && firstOfMonth(d) === this.month;
    if (inMonth(this.focusDay)) return this.focusDay;
    if (selection && inMonth(selection.start)) return selection.start;
    for (let n = 0; n < daysInMonth(this.month); n++) {
      const day = addDays(this.month, n);
      if (this.options.dayState(day).clickable) return day;
    }
    return this.month;
  }

  private dayButton(day: IsoDate, state: DayState, selection: Stay | null, focusable: boolean): HTMLButtonElement {
    const classes = ['day', `day--${state.status}`];
    if (state.insideBlockedPeriod) classes.push('day--blocked');
    const isStart = selection?.start === day;
    const isEnd = selection?.end === day;
    const inSelection = !!selection && day >= selection.start && day <= selection.end;
    if (inSelection) classes.push('day--selected');
    if (isStart) classes.push('day--check-in');
    if (isEnd) classes.push('day--check-out');

    const date = parseIso(day);
    let label = `${WEEKDAY_NAMES[date.getUTCDay()]} ${date.getUTCDate()} ${MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCFullYear()}, `;
    label += state.insideBlockedPeriod && state.status !== 'enquire' ? 'phone or email to book' : STATUS_TEXT[state.status];
    if (isStart) label += ', check-in';
    if (isEnd) label += ', check-out';

    const button = h('button', {
      type: 'button',
      class: classes.join(' '),
      'data-day': day,
      tabindex: focusable ? 0 : -1,
      'aria-label': label,
      'aria-disabled': !state.clickable || this.disabled ? 'true' : null,
      'aria-pressed': inSelection ? 'true' : null,
      onclick: () => this.activate(day),
      onkeydown: (e: Event) => this.onKey(e as KeyboardEvent, day),
    },
      h('span', { class: 'day__number' }, String(date.getUTCDate())),
      isStart ? h('span', { class: 'day__marker', 'aria-hidden': 'true' }, 'in') : null,
      isEnd ? h('span', { class: 'day__marker', 'aria-hidden': 'true' }, 'out') : null,
    );
    return button;
  }

  private activate(day: IsoDate): void {
    if (this.disabled) return;
    const state = this.options.dayState(day);
    this.focusDay = day;
    if (state.clickable) this.options.onActivate(day, state);
  }

  private onKey(e: KeyboardEvent, day: IsoDate): void {
    const moves: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    let target: IsoDate | null = null;
    if (e.key in moves) target = addDays(day, moves[e.key]!);
    else if (e.key === 'Home') target = addDays(day, -((dayOfWeek(day) + 6) % 7));
    else if (e.key === 'End') target = addDays(day, 6 - ((dayOfWeek(day) + 6) % 7));
    else if (e.key === 'PageUp') target = addMonths(day, -1);
    else if (e.key === 'PageDown') target = addMonths(day, 1);
    else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      this.activate(day);
      return;
    }
    if (!target) return;
    e.preventDefault();
    const month = firstOfMonth(target);
    if (month < firstOfMonth(this.options.minMonth) || month > firstOfMonth(this.options.maxMonth)) return;
    this.focusDay = target;
    if (month !== this.month) this.showMonth(month, true);
    else this.render();
    this.focusCurrent();
  }

  private focusCurrent(): void {
    this.options.container.querySelector<HTMLButtonElement>('button.day[tabindex="0"]')?.focus();
  }
}
