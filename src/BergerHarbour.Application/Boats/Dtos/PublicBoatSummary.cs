namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>An active house boat on the booking app's Dates &amp; boat step.</summary>
public sealed record PublicBoatSummary(
    string Slug,
    string Name,
    string? ImageUrl,
    int Berth,
    bool HasSpa,
    bool IsPetFriendly);
