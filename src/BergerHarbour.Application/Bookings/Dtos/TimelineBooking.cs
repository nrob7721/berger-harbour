using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A timeline bar. Holds have no reference yet.</summary>
public sealed record TimelineBooking(
    string Id,
    string BoatId,
    string? Reference,
    string CustomerName,
    BookingStatus Status,
    bool IsStandby,
    PaymentStatus PaymentStatus,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    string CheckInTime,
    string CheckOutTime);
