import { describe, expect, it } from 'vitest';
import { formatMobile, isValidMobile, toE164 } from './phone';

describe('mobile numbers', () => {
  it('accepts mobiles and rejects landlines', () => {
    expect(isValidMobile('0412 345 678')).toBe(true);
    expect(isValidMobile('02 9876 5432')).toBe(false);
    expect(isValidMobile('+1 650 253 0000')).toBe(true); // FIXED_LINE_OR_MOBILE
    expect(toE164('0412 345 678')).toBe('+61412345678');
    expect(formatMobile('+61412345678')).toBe('0412 345 678');
    expect(formatMobile('+447911123456')).toBe('+44 7911 123456');
  });
});
