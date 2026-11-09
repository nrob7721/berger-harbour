using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>The booking fields staff edit in the booking dialog.</summary>
public sealed record StaffBookingDetails(
    BoatId BoatId,
    PeriodType PeriodType,
    StayRange Stay,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    bool IsStandby,
    string? BookingComments,
    decimal HirePrice,
    PaymentScheduleType PaymentSchedule,
    IReadOnlyList<AddonLine> AddonLines);
