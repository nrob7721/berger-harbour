namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>
/// An active house boat on the booking app's Dates &amp; boat step. <c>ThumbnailUrl</c> is the first photo's URL (empty when the boat
/// has no photos); <c>Tags</c> are in the boat's order.
/// </summary>
public sealed record PublicBoatSummary(
    string Slug,
    string Name,
    string ThumbnailUrl,
    int Berth,
    bool HasSpa,
    bool IsPetFriendly,
    IReadOnlyList<string> Tags);
