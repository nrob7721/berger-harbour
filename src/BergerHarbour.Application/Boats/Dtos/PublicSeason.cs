using BergerHarbour.Application.Settings.Dtos;

namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>A season for the booking app's prices table, with its day-month ranges (<c>dd-MM</c>; none for Normal).</summary>
public sealed record PublicSeason(
    string Id,
    string Name,
    IReadOnlyList<SeasonRangeDto> Ranges);
