import { AsYouType, getCountries, getCountryCallingCode, parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js';
import { h } from './dom';

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
 * A phone field: a searchable country selector (type to filter, e.g. "new z") and the number. The stored value is
 * shown in national format for its country.
 */
export function phoneField(options: { name: string; label: string; required?: boolean; value?: string | null; hint?: string }): PhoneField {
  const id = `phone-${options.name}-${++counter}`;
  const listId = `${id}-countries`;
  let country: CountryOption = COUNTRIES[0]!;
  const countryInput = h('input', {
    id: `${id}-country`, class: 'phone__country', list: listId, autocomplete: 'off', 'aria-label': `${options.label} country code`,
    value: country.label,
  });
  const numberInput = h('input', {
    id, name: options.name, type: 'tel', autocomplete: 'tel-national', class: 'phone__number', required: options.required,
    maxlength: 30, inputmode: 'tel',
  });
  const list = h('datalist', { id: listId }, ...COUNTRIES.map((c) => h('option', { value: c.label })));

  const selectCountry = () => {
    const found = findCountry(countryInput.value);
    if (found) country = found;
    countryInput.value = country.label;
  };
  countryInput.addEventListener('change', selectCountry);
  countryInput.addEventListener('blur', selectCountry);
  countryInput.addEventListener('focus', () => countryInput.select());
  numberInput.addEventListener('blur', () => {
    const e164 = toE164(country.code, numberInput.value);
    if (e164) numberInput.value = new AsYouType(country.code).input(numberInput.value);
  });

  const field: PhoneField = {
    element: h('div', { class: 'field phone', 'data-field': options.name },
      h('label', { for: id }, options.label + (options.required ? ' *' : '')),
      h('div', { class: 'phone__row' }, countryInput, numberInput, list),
      options.hint ? h('span', { class: 'field__hint' }, options.hint) : null),
    numberInput,
    value() {
      selectCountry();
      const typed = numberInput.value.trim();
      if (!typed) return null;
      // A number typed with its own +code wins over the selector.
      return toE164(country.code, typed) ?? undefined;
    },
    setValue(stored) {
      const number = stored ? parsePhoneNumberFromString(stored, DEFAULT_COUNTRY) : undefined;
      const match = number?.country ? COUNTRIES.find((c) => c.code === number.country) : undefined;
      country = match ?? COUNTRIES[0]!;
      countryInput.value = country.label;
      numberInput.value = number ? (match ? number.formatNational() : number.formatInternational()) : stored ?? '';
    },
  };
  field.setValue(options.value);
  return field;
}
