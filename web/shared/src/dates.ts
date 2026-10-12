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

/** dd/MM/yyyy — Australian day-first; never month-first. */
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

/** "11am", "11:30am" from "HH:mm" (or "HH:mm:ss"). */
export function formatTime(hhmm: string | null | undefined): string {
  if (!hhmm) return '';
  const [h, m] = hhmm.split(':').map(Number);
  const hour = h! % 12 === 0 ? 12 : h! % 12;
  const suffix = h! < 12 ? 'am' : 'pm';
  return m ? `${hour}:${String(m).padStart(2, '0')}${suffix}` : `${hour}${suffix}`;
}

const sydneyParts = new Intl.DateTimeFormat('en-AU', {
  timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit', hour: 'numeric', minute: '2-digit', hour12: false,
});

/** dd/MM/yyyy h:mma in Australia/Sydney, e.g. "12/10/2026 3:05pm". */
export function formatTimestamp(instant: string | null | undefined): string {
  if (!instant) return '';
  const parts = Object.fromEntries(sydneyParts.formatToParts(new Date(instant)).map((p) => [p.type, p.value]));
  const hour = Number(parts.hour) % 24;
  return `${parts.day}/${parts.month}/${parts.year} ${formatTime(`${hour}:${parts.minute}`)}`;
}

/** The Australia/Sydney date of an instant, as yyyy-MM-dd. */
export function sydneyDate(instant: string): IsoDate {
  return sydneyToday(new Date(instant));
}

const offsetFormat = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', timeZoneName: 'shortOffset' });

/**
 * "AEST" or "AEDT": the Australia/Sydney time zone at 11am on the date. Daylight saving changes at 2–3am, so mid-morning
 * is unambiguous.
 */
export function sydneyZoneLabel(iso: IsoDate): string {
  const d = parseIso(iso);
  const morning = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), 0, 30)); // 10:30–11:30 in Sydney
  const zone = offsetFormat.formatToParts(morning).find((p) => p.type === 'timeZoneName')?.value ?? '';
  return zone.includes('+11') ? 'AEDT' : 'AEST';
}
