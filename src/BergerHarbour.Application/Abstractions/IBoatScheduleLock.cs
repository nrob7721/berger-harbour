using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Abstractions;

/// <summary>
/// Runs work that creates or moves occupancy for a boat in a per-boat exclusive section, so concurrent writes for
/// the same boat are serialised. The work may be retried, so it must load every aggregate it changes through the
/// session and have no side effects other than session writes.
/// </summary>
public interface IBoatScheduleLock
{
    Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default);
}

/// <summary>Transaction-bound reads and writes for one boat's bookings and unavailabilities.</summary>
public interface IBoatScheduleSession
{
    BoatId BoatId { get; }

    Task<Booking?> GetBookingAsync(BookingId id);

    /// <summary>
    /// The boat's Active and PendingPayment bookings (stand-bys included) whose stay overlaps the range.
    /// Callers apply <see cref="Booking.BlocksAvailability"/> to decide what blocks.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetOccupyingBookingsAsync(Stay range);

    Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(Stay range);

    Task<BoatUnavailability?> GetUnavailabilityAsync(UnavailabilityId id);

    /// <summary>Queues an insert (Version 0) or a version-checked update; written when the work completes.</summary>
    void Save(Booking booking);

    void Save(BoatUnavailability unavailability);
}
