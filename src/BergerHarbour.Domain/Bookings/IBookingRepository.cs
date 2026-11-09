using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Booking reads, and writes that don't create or move occupancy (payments, notifications, linking a customer). Writes
/// that can change a boat's occupancy go through the boat schedule lock's session.
/// </summary>
public interface IBookingRepository
{
    Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default);

    /// <summary>Saves with an optimistic version check.</summary>
    Task SaveAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Every booking (any status, any boat) whose stay overlaps <c>[from, to)</c>.</summary>
    Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Active and PendingPayment bookings of any boat that overlap <c>[from, to)</c> and have a line for the add-on.</summary>
    Task<IReadOnlyList<Booking>> ListWithAddonAsync(AddonId addonId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> ListByCustomerAsync(CustomerId customerId, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default);

    /// <summary>Active bookings starting on or after <paramref name="fromStartDate"/>.</summary>
    Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly fromStartDate, CancellationToken ct = default);

    Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Every booking, for search by name, email or reference.</summary>
    Task<IReadOnlyList<Booking>> ListAllAsync(CancellationToken ct = default);
}
