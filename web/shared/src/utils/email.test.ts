import { describe, expect, it } from 'vitest';
import { isValidEmail } from './email';

// The same examples as the server's EmailAddress tests (§7.1).
describe('isValidEmail', () => {
  it.each(['jane@gmail.com', 'jane@bigpond.net.au', 'jane@example.co.uk', '  jane.smith+boats@my-domain.com.au  '])('accepts %s', (email) => {
    expect(isValidEmail(email)).toBe(true);
  });

  it.each([
    'jane@gmail', 'jane@b.c', 'jane@gmail..com', 'jane@-gmail.com', 'jane@gmail-.com', 'jane@gmail.c0m', 'jane smith@gmail.com',
    '@gmail.com', 'jane@@gmail.com', 'jane@x@gmail.com', 'jane@gm_ail.com', '',
  ])('rejects %j', (email) => {
    expect(isValidEmail(email)).toBe(false);
  });

  it('allows at most 254 characters', () => {
    const domain = '@example.com';
    expect(isValidEmail('a'.repeat(254 - domain.length) + domain)).toBe(true);
    expect(isValidEmail('a'.repeat(255 - domain.length) + domain)).toBe(false);
  });
});
