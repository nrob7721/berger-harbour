import type { PeriodType } from '@/lib/types';

/** One row of the Photos tab. */
export interface PhotoRow {
  url: string;
  description: string;
}

/** The Fleet boat dialog's form (§12.2). Everything in it is saved only with the boat. */
export interface BoatForm {
  name: string;
  slug: string;
  berth: number | null;
  adultBeds: number | null;
  kidsBeds: number | null;
  beddingDescription: string;
  securityBond: number | null;
  depositAmount: number | null;
  tags: string[];
  images: PhotoRow[];
  hasSpa: boolean;
  isPetFriendly: boolean;
  isActive: boolean;
  rates: Record<string, Partial<Record<PeriodType, number | null>>>;
  allowedAddonIds: string[];
}

/** Maps the server's per-item field errors (§7.3) to form paths: `images[2].url` → `images.2.url`, and every tag error to `tags`. */
export function boatErrorAliases(keys: string[]): Record<string, string> {
  const aliases: Record<string, string> = {};
  for (const key of keys) {
    const image = /^images\[(\d+)\]\.(url|description)$/.exec(key);
    if (image) aliases[key] = `images.${image[1]}.${image[2]}`;
    else if (key === 'images') aliases[key] = 'images.root';
    else if (/^tags(\[\d+\])?$/.test(key)) aliases[key] = 'tags';
  }
  return aliases;
}
