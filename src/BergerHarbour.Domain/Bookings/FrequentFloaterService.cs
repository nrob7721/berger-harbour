using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// A booking is a Frequent Floater booking when the same customer has at least one other Active non-stand-by booking
/// starting within a year either side of the anchor (§7.8). The perk is decided by staff.
/// </summary>
public sealed class FrequentFloaterService
{
    /// <param name="anchor">When the booking became Active (ActivatedAt), or now for a booking still being created.</param>
    /// <param name="customerBookings">Every booking of the customer.</param>
    /// <param name="self">The booking being evaluated, which never counts.</param>
    public FrequentFloaterResult Evaluate(DateTimeOffset anchor, IEnumerable<Booking> customerBookings, BookingId? self)
    {
        var anchorDate = Sydney.DateOf(anchor);
        var from = anchorDate.AddYears(-1);
        var to = anchorDate.AddYears(1);
        var count = customerBookings.Count(b =>
            b.Id != self && b.Status == BookingStatus.Active && !b.IsStandby && b.StartDate >= from && b.StartDate <= to);
        return new FrequentFloaterResult(count > 0, count);
    }
}
