import { useMemo } from 'react';
import { loadStripe } from '@stripe/stripe-js';
import { EmbeddedCheckout as StripeEmbeddedCheckout, EmbeddedCheckoutProvider } from '@stripe/react-stripe-js';
import { Notice } from '@/components/Notice/Notice';
import { env } from '@/config/env';
import styles from './EmbeddedCheckout.module.css';

let stripePromise: ReturnType<typeof loadStripe> | null = null;
const getStripe = () => (stripePromise ??= loadStripe(env.stripePublishableKey));

/** Stripe Embedded Checkout (hosted Checkout can't run inside the WordPress iframe). */
export function EmbeddedCheckout({ clientSecret, onComplete }: { clientSecret: string; onComplete: () => void }) {
  const options = useMemo(() => ({ clientSecret, onComplete }), [clientSecret, onComplete]);
  if (!env.stripePublishableKey) return <Notice tone="error">Payments aren't configured (missing Stripe publishable key).</Notice>;
  return (
    <div className={styles.checkout} data-testid="embedded-checkout">
      <EmbeddedCheckoutProvider stripe={getStripe()} options={options}>
        <StripeEmbeddedCheckout />
      </EmbeddedCheckoutProvider>
    </div>
  );
}
