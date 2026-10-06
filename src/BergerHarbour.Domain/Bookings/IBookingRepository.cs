using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Reads bookings and saves changes that do not move occupancy (payments, add-ons, notifications, expiry).
/// Writes that create or move occupancy go through the per-boat exclusive section.
/// </summary>
public interface IBookingRepository
{
    Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default);

    Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken ct = default);

    Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Bookings of any boat whose stay overlaps [from, to), optionally only the given statuses.</summary>
    Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, IReadOnlyCollection<BookingStatus>? statuses = null,
        BoatId? boatId = null, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> ListByCustomerAsync(IReadOnlyCollection<CustomerId> customerIds, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default);

    /// <summary>Bookings with the given status whose StartDate is on or after the date.</summary>
    Task<IReadOnlyList<Booking>> ListByStatusStartingFromAsync(BookingStatus status, DateOnly startDateFrom,
        CancellationToken ct = default);

    /// <summary>Updates an existing booking with an optimistic version check.</summary>
    Task SaveAsync(Booking booking, CancellationToken ct = default);
}
