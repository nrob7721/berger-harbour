import { formatDateWithWeekday, zoneLabel } from '@shared/utils/dates';

/** "Fri 16/10/2026, 11am – 2pm AEDT/NSW" — each row carries its own zone label. */
export function checkInRow(start: string): string {
  return `${formatDateWithWeekday(start)}, 11am – 2pm ${zoneLabel(start)}`;
}

/** "Fri 23/10/2026, from 8am AEDT/NSW" */
export function checkOutRow(end: string): string {
  return `${formatDateWithWeekday(end)}, from 8am ${zoneLabel(end)}`;
}
