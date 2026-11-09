import { addDays, dayOfWeek, formatDate, formatTime, type IsoDate } from '@shared/utils/dates';
import { lineTotal, sum } from '@shared/utils/money';
import { periodForClick } from '@shared/utils/periods';
import type { Addon, Boat, PeriodType, TimelineBooking } from '@/lib/types';

export interface LineForm {
  /** Null for a line added in this dialog (not saved yet). */
  id: string | null;
  addonId: string;
  name: string;
  quantity: number;
  unitPrice: number | null;
}

/** The end date pre-filled when staff choose a period type; Custom keeps whatever is there. */
export function defaultEndDate(periodType: PeriodType, start: IsoDate, currentEnd: IsoDate): IsoDate {
  switch (periodType) {
    case 'Midweek': return addDays(start, 4);
    case 'Weekend': return addDays(start, 3);
    case 'Week': return addDays(start, 7);
    case 'LongWeekend': return addDays(start, 4);
    default: return currentEnd > start ? currentEnd : addDays(start, 1);
  }
}

/**
 * A new booking's starting period for a clicked day (or today for "+ New"), with the booking app's click mapping (§11.2, §12.1):
 * Mon–Thu → the Mid-week Mon→Fri of that week; Fri–Sun → the Weekend Fri→Mon containing the day. The start can be before the
 * day. Week, LongWeekend and Custom are never chosen automatically.
 */
export function newBookingPeriod(day: IsoDate): { periodType: 'Midweek' | 'Weekend'; startDate: IsoDate; endDate: IsoDate } {
  const dow = dayOfWeek(day);
  const periodType = dow >= 1 && dow <= 4 ? 'Midweek' : 'Weekend';
  const stay = periodForClick(periodType, day)!; // every Mon–Thu is a Mid-week day and every Fri–Sun a Weekend day
  return { periodType, startDate: stay.start, endDate: stay.end };
}

/** A new line's unit price: the add-on's price for the period type; empty for price on request and LongWeekend/Custom. */
export function settingsPrice(addon: Addon | undefined, periodType: PeriodType): number | null {
  if (!addon || addon.priceOnRequest) return null;
  if (periodType === 'Midweek') return addon.midweekPrice;
  if (periodType === 'Weekend') return addon.weekendPrice;
  if (periodType === 'Week') return addon.weekPrice;
  return null;
}

export function lineAmount(line: Pick<LineForm, 'quantity' | 'unitPrice'>): number | null {
  if (line.unitPrice === null || line.unitPrice === undefined || Number.isNaN(line.unitPrice)) return null;
  return lineTotal(Number(line.quantity) || 0, line.unitPrice);
}

/** Hire price, priced add-ons and total, calculated from the form as it changes. Unpriced lines are left out. */
export function priceBreakdown(hirePrice: number | null, lines: Pick<LineForm, 'quantity' | 'unitPrice'>[], amountPaid: number | null) {
  const amounts = lines.map(lineAmount);
  const addons = sum(amounts.filter((a): a is number => a !== null));
  const hire = hirePrice !== null && !Number.isNaN(hirePrice) ? hirePrice : 0;
  const total = sum([hire, addons]);
  return {
    hire,
    addons,
    total,
    unpriced: amounts.filter((a) => a === null).length,
    paid: amountPaid,
    owing: amountPaid === null ? null : Math.max(0, sum([total, -amountPaid])),
  };
}

/** Non-blocking warnings about guests and beds. */
export function guestWarnings(boat: Boat | undefined, adults: number, children: number): string[] {
  if (!boat) return [];
  const guests = (Number(adults) || 0) + (Number(children) || 0);
  const beds = (boat.adultBeds ?? 0) + (boat.kidsBeds ?? 0);
  if (guests > boat.berth) return [`${guests} guests is more than ${boat.name}'s berth of ${boat.berth}.`];
  if (guests > beds) return [`${guests} guests is more than ${boat.name}'s ${beds} beds; some guests will share beds.`];
  return [];
}

/** A custom time clashing with a same-day changeover on the boat. */
export function changeoverWarnings(
  bookings: TimelineBooking[], boatId: string, selfId: string | null, start: IsoDate, end: IsoDate, checkIn: string, checkOut: string,
): string[] {
  const out: string[] = [];
  for (const b of bookings) {
    if (b.boatId !== boatId || b.id === selfId || b.isStandby || !(b.status === 'Active' || b.status === 'PendingPayment')) continue;
    const label = b.reference ?? 'a booking awaiting payment';
    if (b.endDate === start && checkIn < b.checkOutTime)
      out.push(`Check-in at ${formatTime(checkIn)} on ${formatDate(start)} is before ${label} checks out at ${formatTime(b.checkOutTime)}.`);
    if (b.startDate === end && checkOut > b.checkInTime)
      out.push(`Check-out at ${formatTime(checkOut)} on ${formatDate(end)} is after ${label} checks in at ${formatTime(b.checkInTime)}.`);
  }
  return out;
}

export const PERIOD_TYPES: { value: PeriodType; label: string }[] = [
  { value: 'Midweek', label: 'Mid-week (Mon–Fri)' },
  { value: 'Weekend', label: 'Weekend (Fri–Mon)' },
  { value: 'Week', label: 'Week (7 nights)' },
  { value: 'LongWeekend', label: 'Long weekend' },
  { value: 'Custom', label: 'Custom' },
];
