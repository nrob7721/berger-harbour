import { boatPageUrl, WORDPRESS_HOME, WORDPRESS_ORIGINS } from '@/config/env';

export const CLOSE_MESSAGE = { type: 'bh-booking:close' } as const;

/**
 * Inside the WordPress iframe: ask the page to close the modal. Otherwise (a mobile tab or a direct visit): try
 * window.close(), and if the page is still open a moment later, go to the requested boat's page or the home page.
 */
export function closeBooking(requestedSlug: string | null, win: Window = window, delayMs = 300): void {
  if (win.parent && win.parent !== win) {
    for (const origin of WORDPRESS_ORIGINS) win.parent.postMessage(CLOSE_MESSAGE, origin);
    return;
  }
  win.close();
  win.setTimeout(() => {
    if (!win.closed) win.location.assign(requestedSlug ? boatPageUrl(requestedSlug) : WORDPRESS_HOME);
  }, delayMs);
}
