namespace BergerHarbour.Application.Unavailabilities.Dtos;

/// <summary>A boat unavailability.</summary>
public sealed record UnavailabilityDto(
    string Id,
    string BoatId,
    DateOnly FirstNight,
    DateOnly LastNight,
    string? Comments,
    int Version);
