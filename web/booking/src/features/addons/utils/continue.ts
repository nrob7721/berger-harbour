import type { QueryClient } from '@tanstack/react-query';
import { errorMessage } from '@shared/lib/http';
import { api, keys } from '@/lib/api';
import type { BookingState } from '@/lib/booking-state';
import { draftBody } from '@/lib/draft';
import type { StepOutcome } from '@/lib/progress';
import type { BoatDetails, PublicAddon } from '@/lib/types';

/** Requested add-ons the selected boat doesn't offer. */
export function offeredAddons(addons: Record<string, number>, boat: BoatDetails): Record<string, number> {
  const ids = new Set(boat.addons.map((a) => a.id));
  return Object.fromEntries(Object.entries(addons).filter(([id]) => ids.has(id)));
}

export const addonsRemovedNotice = (boat: string) => `Some add-ons you chose aren't offered on ${boat} and have been removed.`;
export const capacityNotice = (addon: PublicAddon, remaining: number) => `${addon.name}: only ${remaining} ${addon.capacityUnitLabel} remain for your dates.`;

/**
 * The Add-ons Continue for a step a forward jump passes. It stops on Add-ons when add-ons were removed because the boat
 * changed, or a capacity-limited add-on is above what now remains (fetched fresh); otherwise it updates the draft.
 */
export async function passAddons(state: BookingState, queryClient: QueryClient): Promise<StepOutcome> {
  if (!state.selectedSlug || !state.period || !state.draftId) return { ok: false, stop: 'addons' };
  const slug = state.selectedSlug;
  const period = state.period;
  try {
    const boat = await queryClient.fetchQuery({ queryKey: keys.boat(slug), queryFn: () => api.boat(slug), staleTime: Infinity });
    const kept = offeredAddons(state.addons, boat);
    if (Object.keys(kept).length !== Object.keys(state.addons).length) {
      return { ok: false, stop: 'addons', notice: addonsRemovedNotice(boat.name), patch: { addons: kept } };
    }
    for (const addon of boat.addons.filter((a) => a.hasCapacity && kept[a.id] !== undefined)) {
      const { remaining } = await queryClient.fetchQuery({
        queryKey: keys.addonRemaining(addon.id, period.start, period.end),
        queryFn: () => api.addonRemaining(addon.id, period.start, period.end),
        staleTime: 0,
      });
      if (kept[addon.id] > remaining) return { ok: false, stop: 'addons', notice: capacityNotice(addon, remaining) };
    }
    await api.updateDraft(state.draftId, draftBody(state, 'Summary')!);
    return { ok: true };
  } catch (e) {
    return { ok: false, stop: 'addons', notice: errorMessage(e) };
  }
}
