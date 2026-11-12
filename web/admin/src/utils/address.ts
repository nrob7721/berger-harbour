import { z } from 'zod';
import { AUSTRALIA, AUSTRALIAN_STATES, countryName } from '@shared/utils/countries';
import type { Address } from '@/lib/types';

/** The address parts as the booking dialog edits them. Country is always set (Australia by default). */
export interface AddressForm {
  line1: string;
  line2: string;
  locality: string;
  state: string;
  postcode: string;
  country: string;
}

export function toAddressForm(address: Address | null | undefined): AddressForm {
  return {
    line1: address?.line1 ?? '', line2: address?.line2 ?? '', locality: address?.locality ?? '', state: address?.state ?? '',
    postcode: address?.postcode ?? '', country: address?.country ?? AUSTRALIA,
  };
}

/** The request's address: `null` when only the country is set (the server treats that as no address too). */
export function toAddressRequest(form: AddressForm): Address | null {
  const part = (v: string) => v.trim() || null;
  const address = {
    line1: part(form.line1), line2: part(form.line2), locality: part(form.locality), state: part(form.state), postcode: part(form.postcode),
    country: part(form.country),
  };
  const { line1, line2, locality, state, postcode } = address;
  return [line1, line2, locality, state, postcode].some((v) => v !== null) ? address : null;
}

const max = (n: number, label: string) => z.string().refine((v) => v.trim().length <= n, `${label} must be ${n} characters or fewer.`);

/** Mirrors the server's `Address` rules (§7.3): no part required; Australian state list and 4-digit postcode. */
export const addressSchema = z.object({
  line1: max(100, 'Address line 1'),
  line2: max(100, 'Address line 2'),
  locality: max(60, 'City/Locality'),
  state: max(60, 'State'),
  postcode: max(10, 'Postcode'),
  country: z.string(),
}).superRefine((a, ctx) => {
  if (a.country !== AUSTRALIA) return;
  if (a.state.trim() && !AUSTRALIAN_STATES.includes(a.state.trim().toUpperCase()))
    ctx.addIssue({ code: 'custom', path: ['state'], message: 'Choose an Australian state or territory.' });
  if (a.postcode.trim() && !/^\d{4}$/.test(a.postcode.trim()))
    ctx.addIssue({ code: 'custom', path: ['postcode'], message: 'Enter a 4-digit postcode.' });
});

/** The address on several lines for display: line 1, line 2, `City/Locality State Postcode`, country name. Blank parts are skipped. */
export function addressLines(address: Address | null | undefined): string[] {
  if (!address) return [];
  const town = [address.locality, address.state, address.postcode].filter(Boolean).join(' ');
  return [address.line1, address.line2, town, address.country ? countryName(address.country) : null].filter((l): l is string => !!l);
}
