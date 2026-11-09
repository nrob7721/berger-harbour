import { getCountries, getCountryCallingCode, parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js/max';

export const MOBILE_ERROR = 'Enter a valid mobile number for the selected country.';

export interface Country {
  code: CountryCode;
  name: string;
  callingCode: string;
}

let cached: Country[] | null = null;

/** Every country with its calling code, Australia first, then by name. */
export function countries(): Country[] {
  if (cached) return cached;
  const names = new Intl.DisplayNames(['en-AU'], { type: 'region' });
  const list = getCountries().map((code) => ({ code, name: names.of(code) ?? code, callingCode: getCountryCallingCode(code) }));
  list.sort((a, b) => (a.code === 'AU' ? -1 : b.code === 'AU' ? 1 : a.name.localeCompare(b.name)));
  cached = list;
  return list;
}

/** A mobile number (or FIXED_LINE_OR_MOBILE where the two can't be told apart). Landlines are rejected. */
export function isValidMobile(value: string | null | undefined, country: CountryCode = 'AU'): boolean {
  if (!value) return false;
  const n = parsePhoneNumberFromString(value, country);
  if (!n || !n.isValid()) return false;
  const type = n.getType();
  return type === 'MOBILE' || type === 'FIXED_LINE_OR_MOBILE';
}

export function toE164(value: string, country: CountryCode = 'AU'): string | null {
  const n = parsePhoneNumberFromString(value, country);
  return n?.isValid() ? n.number : null;
}

/** National format for Australian numbers, international for others. */
export function formatMobile(e164: string | null | undefined): string {
  if (!e164) return '';
  const n = parsePhoneNumberFromString(e164);
  if (!n) return e164;
  return n.country === 'AU' ? n.formatNational() : n.formatInternational();
}

/** Splits a stored value into country and national number for editing. */
export function splitNumber(value: string | null | undefined): { country: CountryCode; national: string } {
  if (value) {
    const n = parsePhoneNumberFromString(value, 'AU');
    if (n?.country) return { country: n.country, national: n.formatNational() };
  }
  return { country: 'AU', national: value?.startsWith('+') ? '' : (value ?? '') };
}
