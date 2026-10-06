// Calendar dates are ISO strings (yyyy-MM-dd) — Australia/Sydney local dates, never instants.
// Arithmetic uses UTC so the browser's time zone can never shift a day.

export type IsoDate = string;

export function parseIso(iso: IsoDate): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y!, m! - 1, d!));
}

export function toIso(date: Date): IsoDate {
  return date.toISOString().slice(0, 10);
}

export function addDays(iso: IsoDate, days: number): IsoDate {
  const d = parseIso(iso);
  d.setUTCDate(d.getUTCDate() + days);
  return toIso(d);
}

/** 0 = Sunday … 6 = Saturday. */
export function dayOfWeek(iso: IsoDate): number {
  return parseIso(iso).getUTCDay();
}

export function daysBetween(from: IsoDate, to: IsoDate): number {
  return Math.round((parseIso(to).getTime() - parseIso(from).getTime()) / 86_400_000);
}

export function addMonths(iso: IsoDate, months: number): IsoDate {
  const d = parseIso(iso);
  return toIso(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + months, 1)));
}

export function firstOfMonth(iso: IsoDate): IsoDate {
  return iso.slice(0, 8) + '01';
}

export function daysInMonth(iso: IsoDate): number {
  const d = parseIso(iso);
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)).getUTCDate();
}

/** dd/MM/yyyy */
export function formatDate(iso: IsoDate | null | undefined): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

/** "Mon 16/11/2026" */
export function formatLongDate(iso: IsoDate): string {
  return `${WEEKDAYS[dayOfWeek(iso)]} ${formatDate(iso)}`;
}

export function monthTitle(iso: IsoDate): string {
  const d = parseIso(iso);
  return `${MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}`;
}

export function weekdayShort(dow: number): string {
  return WEEKDAYS[dow]!;
}

/** Today in Australia/Sydney. */
export function sydneyToday(now: Date = new Date()): IsoDate {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}
