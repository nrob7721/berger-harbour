using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Abstractions;

/// <summary>Transaction-bound reads and writes for one boat's bookings and unavailabilities.</summary>
public interface IBoatScheduleSession
{
    BoatId BoatId { get; }

    Task<Booking?> GetBookingAsync(BookingId id);

    /// <summary>This boat's Active and PendingPayment bookings (stand-bys included) whose stay overlaps.</summary>
    Task<IReadOnlyList<Booking>> ListOverlappingBookingsAsync(StayRange stay);

    Task<IReadOnlyList<BoatUnavailability>> ListOverlappingUnavailabilitiesAsync(StayRange stay);

    Task<BoatUnavailability?> GetUnavailabilityAsync(BoatUnavailabilityId id);

    /// <summary>Queues a versioned write, applied when the exclusive section completes.</summary>
    void SaveBooking(Booking booking);

    void SaveUnavailability(BoatUnavailability unavailability);

    void DeleteUnavailability(BoatUnavailability unavailability);
}
