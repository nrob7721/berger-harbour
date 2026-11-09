import { useQuery } from '@tanstack/react-query';
import { api, keys } from '@/lib/api';

/** Boats in berth order (largest first), then name. */
export const useBoats = () => useQuery({ queryKey: keys.boats, queryFn: api.boats, staleTime: 60_000 });
/** Add-ons in admin-defined order. */
export const useAddons = () => useQuery({ queryKey: keys.addons, queryFn: api.addons, staleTime: 60_000 });
export const useBusinessSettings = () => useQuery({ queryKey: keys.business, queryFn: api.business, staleTime: 60_000 });
export const useSeasons = () => useQuery({ queryKey: keys.seasons, queryFn: api.seasons, staleTime: 60_000 });
