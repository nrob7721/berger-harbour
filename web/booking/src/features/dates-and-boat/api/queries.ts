import { useQuery } from '@tanstack/react-query';
import { api, keys } from '@/lib/api';

/** Every active house boat's unavailable ranges for a window, cached for the session. */
export function useFleetAvailability(from: string, to: string) {
  return useQuery({ queryKey: keys.availability(from, to), queryFn: () => api.availability(from, to), staleTime: Infinity });
}
