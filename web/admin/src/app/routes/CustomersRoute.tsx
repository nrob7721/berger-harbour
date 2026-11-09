import { useState } from 'react';
import { BookingDialog } from '@/features/bookings/components/BookingDialog';
import { CustomersPage } from '@/features/customers/components/CustomersPage';

/** Customers opens the same booking dialog as the timeline; the two features are composed here, not imported into each other. */
export function CustomersRoute() {
  const [bookingId, setBookingId] = useState<string | null>(null);
  return (
    <>
      <CustomersPage onOpenBooking={setBookingId} />
      {bookingId && <BookingDialog bookingId={bookingId} onClose={() => setBookingId(null)} />}
    </>
  );
}
