import { addDays, dayOfWeek, formatDate, formatTime, todayInSydney, type IsoDate } from '@shared/utils/dates';
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

/** A payment row in the booking dialog (decision 38). Rows without an id were added in this dialog and aren't saved yet. */
export interface PaymentForm {
  id: string | null;
  amount: number;
  /** The Sydney date it was paid. */
  date: IsoDate;
  note: string;
  method: 'Stripe' | 'Manual';
}

export const TIME_ERROR = 'Enter a 24-hour time, e.g. 14:30.';

/**
 * A typed 24-hour time as `HH:mm`, or null when it isn't one. Accepts `HH:mm`, `H:mm`, `HHmm` and `Hmm` from 00:00 to 23:59
 * (`9:30` → `09:30`, `1730` → `17:30`, `930` → `09:30`).
 */
export function parseTime24(raw: string | null | undefined): string | null {
  const m = /^(\d{1,2}):?(\d{2})$/.exec((raw ?? '').trim());
  if (!m) return null;
  const hours = Number(m[1]);
  const minutes = Number(m[2]);
  if (hours > 23 || minutes > 59) return null;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
}

export const PAYMENT_BEFORE_CREATION = (created: IsoDate) => `The payment date can't be before the booking was created (${formatDate(created)}).`;
export const PAYMENT_IN_FUTURE = "The payment date can't be in the future.";

/**
 * The manual payment date rule (§7.3): on or after the booking's creation date and on or before today, both as Sydney dates.
 * A new booking is created today, so today is the only valid date. Returns the error, or null.
 */
export function paymentDateError(date: IsoDate, createdOn: IsoDate, today: IsoDate = todayInSydney()): string | null {
  if (date < createdOn) return PAYMENT_BEFORE_CREATION(createdOn);
  if (date > today) return PAYMENT_IN_FUTURE;
  return null;
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

/**
 * The breakdown's Deposit (§12.1): what saving will store (§7.3). While the form has no saved payment rows it is the chosen
 * boat's effective deposit; with a saved payment row, and on holds, it is the booking's stored deposit. Falls back to the stored
 * deposit while the boat's isn't known; `null` ("—") on a new booking without a boat.
 */
export function breakdownDeposit({ boatDeposit, storedDeposit, hasSavedPayment, isHold }: {
  boatDeposit: number | null;
  storedDeposit: number | null;
  hasSavedPayment: boolean;
  isHold: boolean;
}): number | null {
  if (isHold || hasSavedPayment) return storedDeposit;
  return boatDeposit ?? storedDeposit;
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
