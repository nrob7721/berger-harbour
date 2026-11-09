namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>A pricing season.</summary>
public sealed record SeasonDto(
    string Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<SeasonRangeDto> Ranges,
    int Version);
