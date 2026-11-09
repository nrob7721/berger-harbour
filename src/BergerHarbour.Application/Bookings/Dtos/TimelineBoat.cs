namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A timeline row: every boat, active or not (berth desc, then name).</summary>
public sealed record TimelineBoat(
    string Id,
    string Name,
    string Slug,
    int Berth,
    bool IsActive);
