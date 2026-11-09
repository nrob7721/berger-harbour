namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>One boat's unavailable ranges (blocking bookings and unavailabilities). Never booking data.</summary>
public sealed record BoatAvailability(
    string Slug,
    IReadOnlyList<StayRangeDto> Unavailable);
