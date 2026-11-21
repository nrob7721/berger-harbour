import { useSyncExternalStore } from 'react';

/** The booking app's only breakpoint (§11.2). Every media query in the app uses the same width. */
export const SMALL_SCREEN_QUERY = '(max-width: 768px)';

const media = () => (typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(SMALL_SCREEN_QUERY) : null);

function subscribe(onChange: () => void) {
  const m = media();
  m?.addEventListener('change', onChange);
  return () => m?.removeEventListener('change', onChange);
}

/** Is the viewport a small screen (≤ 768px)? A step renders its step action bar or its inline layout, not both. */
export function useSmallScreen(): boolean {
  return useSyncExternalStore(subscribe, () => media()?.matches ?? false, () => false);
}
