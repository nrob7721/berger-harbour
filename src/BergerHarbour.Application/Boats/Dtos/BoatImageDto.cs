namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>One of a boat's photos, in the boat's order; the first is the thumbnail image.</summary>
public sealed record BoatImageDto(
    string Url,
    string? Description);
