using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Abstractions;

/// <summary>Issues BH-YYNNNN references from a per-year counter incremented transactionally.</summary>
public interface IBookingReferenceGenerator
{
    Task<BookingReference> NextAsync(int year, CancellationToken ct = default);
}
