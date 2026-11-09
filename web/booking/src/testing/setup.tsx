import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, vi } from 'vitest';
import { resetApi, server } from './server';

vi.mock('@marsidev/react-turnstile', () => ({
  Turnstile: ({ onSuccess }: { onSuccess: (token: string) => void }) => {
    queueMicrotask(() => onSuccess('turnstile-ok'));
    return null;
  },
}));

vi.mock('@/components/EmbeddedCheckout/EmbeddedCheckout', () => ({
  EmbeddedCheckout: ({ clientSecret, onComplete }: { clientSecret: string; onComplete: () => void }) => (
    <div data-testid="embedded-checkout" data-secret={clientSecret}>
      <button type="button" onClick={onComplete}>Simulate payment</button>
    </div>
  ),
}));

Object.defineProperty(window, 'scrollTo', { value: () => {}, writable: true });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  resetApi();
  window.sessionStorage.clear();
});
afterAll(() => server.close());
