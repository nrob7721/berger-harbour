using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>The completion screen's status: the reference once assigned and a basic summary.</summary>
public sealed record BookingStatusResponse(
    BookingStatus Status,
    string? Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Email);
