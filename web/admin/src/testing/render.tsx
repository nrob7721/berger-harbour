import type { ReactNode } from 'react';
import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router';
import { ToastProvider } from '@shared/components/Toast/Toast';

export function renderAdmin(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return {
    client,
    ...render(
      <QueryClientProvider client={client}>
        <ToastProvider timeoutMs={60_000}>
          <MemoryRouter>{ui}</MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>,
    ),
  };
}
