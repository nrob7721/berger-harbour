import { lazy, Suspense } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { Spinner } from '@shared/components/Spinner/Spinner';
import { ToastProvider } from '@shared/components/Toast/Toast';
import { Layout } from '@/components/Layout/Layout';

// Each page is its own chunk.
const BookingsPage = lazy(() => import('@/features/bookings/components/BookingsPage').then((m) => ({ default: m.BookingsPage })));
const FleetPage = lazy(() => import('@/features/fleet/components/FleetPage').then((m) => ({ default: m.FleetPage })));
const SettingsPage = lazy(() => import('@/features/settings/components/SettingsPage').then((m) => ({ default: m.SettingsPage })));
const CustomersRoute = lazy(() => import('./routes/CustomersRoute').then((m) => ({ default: m.CustomersRoute })));

export function createQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false }, mutations: { retry: false } } });
}

export function AppRoutes() {
  return (
    <Layout>
      <Suspense fallback={<p><Spinner /> Loading…</p>}>
      <Routes>
        <Route path="/" element={<Navigate to="/bookings" replace />} />
        <Route path="/bookings" element={<BookingsPage />} />
        <Route path="/fleet" element={<FleetPage />} />
        <Route path="/customers" element={<CustomersRoute />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/bookings" replace />} />
      </Routes>
      </Suspense>
    </Layout>
  );
}

export function App({ client = createQueryClient() }: { client?: QueryClient }) {
  return (
    <QueryClientProvider client={client}>
      <ToastProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
}
