using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>A validated online booking request, priced on the server.</summary>
public sealed record PreparedOnlineBooking(
    Boat Boat,
    ReferenceData Reference,
    StayRange Stay,
    Season Season,
    decimal HirePrice,
    IReadOnlyList<AddonLine> AddonLines,
    decimal DepositAmount,
    decimal CheckoutAmount,
    PaymentScheduleType PaymentSchedule,
    string? RoomingWarning)
{
    public decimal TotalPrice => HirePrice + AddonLines.Sum(l => l.LineTotal ?? 0m);
}
