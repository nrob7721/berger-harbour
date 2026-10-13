import { getCountries, getCountryCallingCode, parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js';
import { h } from './dom';
import { PhoneInput } from './phone-input';

// Phone numbers are sent to the API in E.164 (+61412345678). Bookings may come from overseas, so every phone field has a
// searchable country code selector that defaults to Australia (+61).

export const DEFAULT_COUNTRY: CountryCode = 'AU';

const regionNames = typeof Intl.DisplayNames === 'function' ? new Intl.DisplayNames(['en-AU'], { type: 'region' }) : null;

export interface CountryOption {
  code: CountryCode;
  label: string;
}

/** Every country as "Australia (+61)", Australia first, the rest by name. */
export const COUNTRIES: CountryOption[] = getCountries()
  .map((code) => ({ code, label: `${regionNames?.of(code) ?? code} (+${getCountryCallingCode(code)})` }))
  .sort((a, b) => (a.code === DEFAULT_COUNTRY ? -1 : b.code === DEFAULT_COUNTRY ? 1 : a.label.localeCompare(b.label)));

/** The E.164 form of a number typed for a country, or null when it isn't a valid number. */
export function toE164(country: CountryCode, typed: string): string | null {
  const number = parsePhoneNumberFromString(typed.trim(), country);
  return number && number.isValid() ? number.number : null;
}

/** National format for Australian numbers, international format for others; anything unparsable as stored. */
export function formatPhone(stored: string | null | undefined): string {
  if (!stored) return '';
  const number = parsePhoneNumberFromString(stored, DEFAULT_COUNTRY);
  if (!number) return stored;
  return number.country === DEFAULT_COUNTRY ? number.formatNational() : number.formatInternational();
}

/** Finds the country for "Australia (+61)", "australia", "+61" or "AU". */
export function findCountry(text: string): CountryOption | null {
  const t = text.trim().toLowerCase();
  if (!t) return null;
  return COUNTRIES.find((c) => c.label.toLowerCase() === t)
    ?? COUNTRIES.find((c) => c.code.toLowerCase() === t)
    ?? COUNTRIES.find((c) => c.label.toLowerCase().startsWith(t))
    ?? COUNTRIES.find((c) => t.startsWith('+') && c.label.endsWith(`(${t})`))
    ?? null;
}

export interface PhoneField {
  /** The labelled wrapper (data-field = the API field name). */
  element: HTMLElement;
  numberInput: HTMLInputElement;
  /** E.164, null when empty, undefined when something was typed that isn't a valid number. */
  value(): string | null | undefined;
  setValue(stored: string | null | undefined): void;
}

let counter = 0;

/**
 * A phone field: a searchable country selector (see PhoneInput) and the number. The stored value is shown in
 * national format for its country.
 */
export function phoneField(options: { name: string; label: string; required?: boolean; value?: string | null; hint?: string }): PhoneField {
  const id = `phone-${options.name}-${++counter}`;
  const input = new PhoneInput({ id, label: options.label, required: options.required ?? false, mobile: options.name === 'mobile' });

  const field: PhoneField = {
    element: h('div', { class: 'field phone', 'data-field': options.name },
      h('label', { for: id }, options.label + (options.required ? ' *' : '')),
      input.element,
      options.hint ? h('span', { class: 'field__hint' }, options.hint) : null),
    numberInput: input.input,
    value() {
      if (!input.input.value.trim()) return null;
      // A number typed with its own +code wins over the selector.
      return input.value ?? undefined;
    },
    setValue(stored) {
      input.value = stored;
    },
  };
  field.setValue(options.value);
  return field;
}
