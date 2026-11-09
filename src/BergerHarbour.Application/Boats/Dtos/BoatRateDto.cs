using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>A rate for a season and period type.</summary>
public sealed record BoatRateDto(
    string SeasonId,
    PeriodType PeriodType,
    decimal Price);
