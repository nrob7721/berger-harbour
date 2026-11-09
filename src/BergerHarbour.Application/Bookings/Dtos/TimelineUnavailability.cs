namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A grey unavailability block.</summary>
public sealed record TimelineUnavailability(
    string Id,
    string BoatId,
    DateOnly FirstNight,
    DateOnly LastNight,
    string? Comments);
