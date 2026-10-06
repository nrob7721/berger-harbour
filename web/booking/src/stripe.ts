// Stripe Embedded Checkout. Stripe.js must be loaded from js.stripe.com (never bundled).

interface EmbeddedCheckoutPage {
  mount(selector: string | HTMLElement): void;
  destroy(): void;
}

interface StripeInstance {
  createEmbeddedCheckoutPage(options: {
    fetchClientSecret: () => Promise<string>;
    onComplete?: () => void;
  }): Promise<EmbeddedCheckoutPage>;
}

declare global {
  interface Window {
    Stripe?: (publishableKey: string) => StripeInstance;
  }
}

const STRIPE_JS = 'https://js.stripe.com/endive/stripe.js';
let loading: Promise<StripeInstance> | null = null;

function loadStripe(): Promise<StripeInstance> {
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined;
  if (!key) return Promise.reject(new Error('Payments are not configured (missing Stripe publishable key).'));
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = STRIPE_JS;
    script.async = true;
    script.onload = () => (window.Stripe ? resolve(window.Stripe(key)) : reject(new Error('Stripe failed to load.')));
    script.onerror = () => {
      loading = null;
      reject(new Error('We could not load the payment form. Please check your connection and try again.'));
    };
    document.head.append(script);
  });
  return loading;
}

let current: EmbeddedCheckoutPage | null = null;

/** Mounts Embedded Checkout for an existing session; resolves when the customer completes payment. */
export async function mountCheckout(container: HTMLElement, clientSecret: string): Promise<void> {
  const stripe = await loadStripe();
  current?.destroy();
  return new Promise<void>((resolve, reject) => {
    stripe
      .createEmbeddedCheckoutPage({
        fetchClientSecret: () => Promise.resolve(clientSecret),
        onComplete: () => {
          current?.destroy();
          current = null;
          resolve();
        },
      })
      .then((checkout) => {
        current = checkout;
        checkout.mount(container);
      })
      .catch(reject);
  });
}
