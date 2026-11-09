using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A search hit by name, email or reference.</summary>
public sealed record BookingSearchResult(
    string Id,
    string? Reference,
    string CustomerName,
    string Email,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    BookingStatus Status,
    bool IsStandby);
