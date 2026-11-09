import { lineTotal, sum } from '@shared/utils/money';
import type { OnlinePeriodType } from '@shared/utils/periods';
import type { PublicAddon } from '@/lib/types';

export function addonPrice(addon: PublicAddon, type: OnlinePeriodType): number | null {
  if (addon.priceOnRequest) return null;
  return (type === 'Midweek' ? addon.prices.midweek : type === 'Weekend' ? addon.prices.weekend : addon.prices.week) ?? null;
}

/** Base hire rate plus every ticked add-on that has a price; flags price-on-request add-ons. */
export function bookingPrice(hirePrice: number, addons: PublicAddon[], ticked: Record<string, number>, type: OnlinePeriodType) {
  const chosen = addons.filter((a) => ticked[a.id] !== undefined);
  const priced = chosen.map((a) => ({ a, price: addonPrice(a, type) })).filter((x) => x.price !== null);
  return {
    total: sum([hirePrice, ...priced.map((x) => lineTotal(ticked[x.a.id], x.price!))]),
    hasPriceOnRequest: chosen.some((a) => addonPrice(a, type) === null),
  };
}
