/** The same email rule as the server's `EmailAddress` value object (§7.1). */
export const EMAIL_ERROR = 'Enter a valid email address.';

const LABEL = /^[A-Za-z0-9-]+$/;
const TOP_LEVEL_DOMAIN = /^[A-Za-z]{2,}$/;

/**
 * After trimming: at most 254 characters; exactly one `@`; a non-empty local part without spaces; a domain of two or more
 * dot-separated labels of letters, digits and hyphens, none empty or starting or ending with `-`; a top-level domain of at
 * least 2 letters.
 */
export function isValidEmail(value: string | null | undefined): boolean {
  const email = (value ?? '').trim();
  if (email.length === 0 || email.length > 254) return false;
  const parts = email.split('@');
  if (parts.length !== 2) return false;
  const [local, domain] = parts;
  if (local.length === 0 || /\s/.test(local)) return false;
  const labels = domain.split('.');
  if (labels.length < 2) return false;
  if (labels.some((l) => !LABEL.test(l) || l.startsWith('-') || l.endsWith('-'))) return false;
  return TOP_LEVEL_DOMAIN.test(labels[labels.length - 1]);
}

/** Lower-cased and trimmed, as the server stores it. */
export const normaliseEmail = (value: string) => value.trim().toLowerCase();
