// Calendar dates are ISO `yyyy-MM-dd` strings (Australia/Sydney local dates). Everything shown is dd/MM/yyyy.

export type IsoDate = string;

const SYDNEY = 'Australia/Sydney';
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

function parts(iso: IsoDate): [number, number, number] {
  const [y, m, d] = iso.split('-').map(Number);
  return [y, m, d];
}

function utc(iso: IsoDate): Date {
  const [y, m, d] = parts(iso);
  return new Date(Date.UTC(y, m - 1, d));
}

export function toIso(date: Date): IsoDate {
  return date.toISOString().slice(0, 10);
}

export function isoOf(year: number, month: number, day: number): IsoDate {
  return toIso(new Date(Date.UTC(year, month - 1, day)));
}

export function addDays(iso: IsoDate, days: number): IsoDate {
  const d = utc(iso);
  d.setUTCDate(d.getUTCDate() + days);
  return toIso(d);
}

export function addMonths(iso: IsoDate, months: number): IsoDate {
  const [y, m] = parts(iso);
  return isoOf(y, m + months, 1);
}

export function daysBetween(from: IsoDate, to: IsoDate): number {
  return Math.round((utc(to).getTime() - utc(from).getTime()) / 86_400_000);
}

/** 0 = Sunday … 6 = Saturday. */
export function dayOfWeek(iso: IsoDate): number {
  return utc(iso).getUTCDay();
}

export function yearMonth(iso: IsoDate): { year: number; month: number } {
  const [y, m] = parts(iso);
  return { year: y, month: m };
}

export function dayOfMonth(iso: IsoDate): number {
  return parts(iso)[2];
}

export function startOfMonth(iso: IsoDate): IsoDate {
  const [y, m] = parts(iso);
  return isoOf(y, m, 1);
}

export function endOfMonthExclusive(iso: IsoDate): IsoDate {
  return addMonths(iso, 1);
}

/** The Monday on or before the date. */
export function mondayOf(iso: IsoDate): IsoDate {
  return addDays(iso, -((dayOfWeek(iso) + 6) % 7));
}

export function monthTitle(iso: IsoDate): string {
  const { year, month } = yearMonth(iso);
  return `${MONTHS[month - 1]} ${year}`;
}

/** dd/MM/yyyy */
export function formatDate(iso: IsoDate | null | undefined): string {
  if (!iso) return '';
  const [y, m, d] = parts(iso);
  return `${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}/${y}`;
}

/** "Fri 16/10/2026" */
export function formatDateWithWeekday(iso: IsoDate): string {
  return `${WEEKDAYS[dayOfWeek(iso)]} ${formatDate(iso)}`;
}

export function weekdayShort(iso: IsoDate): string {
  return WEEKDAYS[dayOfWeek(iso)];
}

/** "11:00" → "11am", "11:30" → "11:30am". */
export function formatTime(hhmm: string): string {
  const [h, min] = hhmm.split(':').map(Number);
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  const suffix = h < 12 ? 'am' : 'pm';
  return min === 0 ? `${hour12}${suffix}` : `${hour12}:${String(min).padStart(2, '0')}${suffix}`;
}

/** Today in Australia/Sydney. */
export function todayInSydney(now: Date = new Date()): IsoDate {
  return new Intl.DateTimeFormat('en-CA', { timeZone: SYDNEY, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}

/** dd/MM/yyyy h:mma in Sydney time. */
export function formatTimestamp(instant: string | null | undefined): string {
  if (!instant) return '';
  const d = new Date(instant);
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-AU', {
      timeZone: SYDNEY, year: 'numeric', month: '2-digit', day: '2-digit', hour: 'numeric', minute: '2-digit', hour12: false,
    }).formatToParts(d).map((x) => [x.type, x.value]),
  );
  const hour = Number(p.hour) % 24;
  return `${p.day}/${p.month}/${p.year} ${formatTime(`${hour}:${p.minute}`)}`;
}

/** "AEST/NSW" or "AEDT/NSW", whichever applies on that date in Australia/Sydney. */
export function zoneLabel(iso: IsoDate): string {
  const [y, m, d] = parts(iso);
  const noon = new Date(Date.UTC(y, m - 1, d, 2)); // ~noon in Sydney
  const offset = new Intl.DateTimeFormat('en-US', { timeZone: SYDNEY, timeZoneName: 'shortOffset' })
    .formatToParts(noon)
    .find((x) => x.type === 'timeZoneName')?.value;
  return offset === 'GMT+11' ? 'AEDT/NSW' : 'AEST/NSW';
}

/** The weeks (Monday first) that contain days of the month, as rows of seven ISO dates. */
export function monthGrid(monthIso: IsoDate): IsoDate[][] {
  const first = startOfMonth(monthIso);
  const nextMonth = endOfMonthExclusive(monthIso);
  const weeks: IsoDate[][] = [];
  for (let monday = mondayOf(first); monday < nextMonth; monday = addDays(monday, 7)) {
    weeks.push(Array.from({ length: 7 }, (_, i) => addDays(monday, i)));
  }
  return weeks;
}

export function isSameMonth(a: IsoDate, b: IsoDate): boolean {
  return a.slice(0, 7) === b.slice(0, 7);
}
