import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import '@shared/styles/tokens.css';
import '@shared/styles/reset.css';
import { BookingApp } from './BookingApp';
import { createQueryClient } from './queryClient';

const params = new URLSearchParams(window.location.search);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={createQueryClient()}>
      <BookingApp
        requestedSlug={params.get('boat')}
        returnedBooking={params.get('booking') && params.get('session_id') ? { bookingId: params.get('booking')!, sessionId: params.get('session_id')! } : null}
      />
    </QueryClientProvider>
  </StrictMode>,
);
