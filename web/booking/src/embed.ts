// The app runs either inside the WordPress modal iframe (desktop) or in its own tab (mobile).

const PARENT_ORIGINS = ['https://bergerhouseboats.com.au', 'https://www.bergerhouseboats.com.au'];

export const isEmbedded = (() => {
  try {
    return window.self !== window.top;
  } catch {
    return true;
  }
})();

/** Asks the WordPress page to close the modal. */
export function requestClose(): void {
  if (!isEmbedded) {
    window.close();
    return;
  }
  for (const origin of PARENT_ORIGINS) {
    window.parent.postMessage({ type: 'bh-booking:close' }, origin);
  }
}
