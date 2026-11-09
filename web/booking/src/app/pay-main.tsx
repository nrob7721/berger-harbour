import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import '@shared/styles/tokens.css';
import '@shared/styles/reset.css';
import { PayPage } from '@/features/balance-payment/components/PayPage';
import { Shell } from './Shell';
import { createQueryClient } from './queryClient';

const params = new URLSearchParams(window.location.search);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={createQueryClient()}>
      <Shell title="Make a payment">
        <PayPage token={params.get('token') ?? ''} paid={params.get('paid') === '1'} />
      </Shell>
    </QueryClientProvider>
  </StrictMode>,
);
