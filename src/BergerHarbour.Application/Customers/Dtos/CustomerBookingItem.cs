using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers.Dtos;

/// <summary>One of a customer's bookings on the Customers page.</summary>
public sealed record CustomerBookingItem(
    string Id,
    string? Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    bool IsStandby);
