namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>Create or edit a season (version 0 when creating).</summary>
public sealed record SaveSeasonRequest(
    string Name,
    IReadOnlyList<SeasonRangeDto>? Ranges,
    int Version);
