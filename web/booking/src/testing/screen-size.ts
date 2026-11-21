import { act } from '@testing-library/react';

/**
 * A `matchMedia` stand-in for jsdom: every query matches while the screen is set small, so `useSmallScreen()` follows it.
 * Tests start on a desktop screen (reset after each test).
 */
let small = false;
const listeners = new Set<() => void>();

export function installMatchMedia() {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      get matches() {
        return small;
      },
      media: query,
      onchange: null,
      addEventListener: (_: string, l: () => void) => listeners.add(l),
      removeEventListener: (_: string, l: () => void) => listeners.delete(l),
      addListener: (l: () => void) => listeners.add(l),
      removeListener: (l: () => void) => listeners.delete(l),
      dispatchEvent: () => false,
    }),
  });
}

/** Switch between a small screen (≤ 768px) and a desktop one. */
export function setSmallScreen(value: boolean) {
  small = value;
  act(() => listeners.forEach((l) => l()));
}

export function resetScreenSize() {
  small = false;
  listeners.clear();
}
