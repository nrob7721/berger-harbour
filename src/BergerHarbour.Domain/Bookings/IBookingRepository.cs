using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

public interface IBookingRepository
{
    Task<Booking?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>Saves outside the per-boat lock (payments, notifications, status changes that free dates).</summary>
    Task SaveAsync(Booking booking, CancellationToken ct = default);
    /// <summary>Bookings of any boat whose stay overlaps [from, to), with one of the statuses.</summary>
    Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, IReadOnlyCollection<BookingStatus> statuses, CancellationToken ct = default);
    /// <summary>Active and PendingPayment bookings of any boat with a line for the add-on and a stay overlapping [from, to).</summary>
    Task<IReadOnlyList<Booking>> ListWithAddonAsync(string addonId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> ListByCustomerAsync(string customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> ListByCustomersAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> ListWithoutCustomerAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default);
    /// <summary>Active bookings starting on or after the date.</summary>
    Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly from, CancellationToken ct = default);
    /// <summary>Bookings whose reference starts with the (upper-case) prefix.</summary>
    Task<IReadOnlyList<Booking>> ListByReferencePrefixAsync(string upperPrefix, CancellationToken ct = default);
    Task<Booking?> GetByPaymentLinkHashAsync(string tokenHash, CancellationToken ct = default);
}

public static class BookingStatuses
{
    public static readonly BookingStatus[] Blocking = [BookingStatus.Active, BookingStatus.PendingPayment];
    public static readonly BookingStatus[] All = Enum.GetValues<BookingStatus>();
}
