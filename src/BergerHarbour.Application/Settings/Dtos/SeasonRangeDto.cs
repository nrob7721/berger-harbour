namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>A recurring range, dd-MM → dd-MM.</summary>
public sealed record SeasonRangeDto(
    string Start,
    string End);
