namespace BergerHarbour.Application.Unavailabilities.Dtos;

/// <summary>Create or edit an unavailability (version 0 when creating).</summary>
public sealed record SaveUnavailabilityRequest(
    DateOnly FirstNight,
    DateOnly LastNight,
    string? Comments,
    int Version);
