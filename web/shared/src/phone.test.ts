import { describe, expect, it } from 'vitest';
import { COUNTRIES, findCountry, formatPhone, toE164 } from './phone';

describe('phone numbers', () => {
  it('defaults to Australia and lists every country', () => {
    expect(COUNTRIES[0]).toEqual({ code: 'AU', label: 'Australia (+61)' });
    expect(COUNTRIES.length).toBeGreaterThan(200);
  });

  it('finds a country by name, code or calling code', () => {
    expect(findCountry('new z')?.code).toBe('NZ');
    expect(findCountry('GB')?.code).toBe('GB');
    expect(findCountry('Australia (+61)')?.code).toBe('AU');
    expect(findCountry('zzzz')).toBeNull();
  });

  it('converts to E.164 for the chosen country', () => {
    expect(toE164('AU', '0412 345 678')).toBe('+61412345678');
    expect(toE164('NZ', '021 123 4567')).toBe('+64211234567');
    expect(toE164('AU', '+44 7911 123456')).toBe('+447911123456');
    expect(toE164('AU', '0400')).toBeNull();
  });

  it('shows Australian numbers nationally and others internationally', () => {
    expect(formatPhone('+61412345678')).toBe('0412 345 678');
    expect(formatPhone('+447911123456')).toBe('+44 7911 123456');
    expect(formatPhone('legacy')).toBe('legacy');
  });
});
