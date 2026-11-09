/** Build-time keys (Vite inlines VITE_*). Local development falls back to Cloudflare's always-pass Turnstile test key. */
export const env = {
  stripePublishableKey: import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY ?? '',
  turnstileSiteKey: import.meta.env.VITE_TURNSTILE_SITE_KEY || '1x00000000000000000000AA',
};

export const WORDPRESS_ORIGINS = ['https://bergerhouseboats.com.au', 'https://www.bergerhouseboats.com.au'];
export const WORDPRESS_HOME = 'https://bergerhouseboats.com.au/';
export const boatPageUrl = (slug: string) => `https://bergerhouseboats.com.au/houseboats/${slug}/`;
