import { useRef, type TouchEvent } from 'react';

/** The least horizontal travel that counts as a swipe (§11.2). */
export const SWIPE_DISTANCE = 50;

export interface SwipeHandlers {
  onTouchStart: (e: TouchEvent) => void;
  onTouchEnd: (e: TouchEvent) => void;
  onTouchCancel: () => void;
}

/**
 * A horizontal touch swipe (§11.2), used by the calendar: at least 50px sideways and further sideways than up
 * or down. Swiping left calls `onSwipe('next')`, swiping right `onSwipe('previous')`. It never prevents the page scrolling
 * vertically (pair it with `touch-action: pan-y`), and a shorter or mostly vertical movement does nothing.
 */
export function useSwipe(onSwipe: (direction: 'next' | 'previous') => void, enabled = true): SwipeHandlers {
  const start = useRef<{ x: number; y: number } | null>(null);
  return {
    onTouchStart: (e) => {
      const t = e.touches[0];
      start.current = enabled && e.touches.length === 1 && t ? { x: t.clientX, y: t.clientY } : null;
    },
    onTouchEnd: (e) => {
      const from = start.current;
      start.current = null;
      const t = e.changedTouches[0];
      if (!enabled || !from || !t) return;
      const dx = t.clientX - from.x;
      const dy = t.clientY - from.y;
      if (Math.abs(dx) < SWIPE_DISTANCE || Math.abs(dx) <= Math.abs(dy)) return;
      onSwipe(dx < 0 ? 'next' : 'previous');
    },
    onTouchCancel: () => {
      start.current = null;
    },
  };
}
