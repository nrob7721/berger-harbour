import { useQuery } from '@tanstack/react-query';
import type { OnlinePeriodType, Stay } from '@shared/utils/periods';
import { api, keys } from '@/lib/api';

/** Active house boats and the booking settings. Also wakes the API on load. */
export function useFleet() {
  return useQuery({ queryKey: keys.boats, queryFn: api.boats, staleTime: Infinity });
}

/** Customer-safe details of one boat (beds, bedding, deposit, allowed add-ons), cached per slug for the session. */
export function useBoatDetails(slug: string | null | undefined) {
  return useQuery({ queryKey: keys.boat(slug ?? ''), queryFn: () => api.boat(slug!), enabled: !!slug, staleTime: Infinity, retry: false });
}

/** Each boat's availability and hire price for the selected period, cached per (period type, start date). */
export function usePeriodAvailability(type: OnlinePeriodType, period: Stay | null) {
  return useQuery({
    queryKey: keys.period(type, period?.start ?? ''),
    queryFn: () => api.periodAvailability(type, period!.start),
    enabled: !!period,
    staleTime: Infinity,
    retry: false,
  });
}
