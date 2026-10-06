using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Boats;

public sealed record BoatSummaryDto(
    string Id,
    string Name,
    string Slug,
    BoatType Type,
    int MaxNoOfGuests,
    int? NoOfBeds,
    bool IsActive);

public sealed record BoatRateDto(string SeasonId, PeriodType PeriodType, decimal Price);

public sealed record BoatDetailDto(
    string Id,
    int Version,
    string Name,
    string Slug,
    BoatType Type,
    int MaxNoOfGuests,
    int? NoOfBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    IReadOnlyList<BoatRateDto> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    bool IsActive,
    IReadOnlyList<string> MissingActivationData,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);

public sealed record BoatUpsertRequest(
    int Version,
    string Name,
    string Slug,
    int MaxNoOfGuests,
    int? NoOfBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    IReadOnlyList<BoatRateDto>? Rates,
    IReadOnlyList<string>? AllowedAddonIds,
    bool IsActive);

public sealed record UnavailabilityDto(
    string Id,
    int Version,
    string BoatId,
    DateOnly FirstNight,
    DateOnly LastNight,
    string? Comments,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);

public sealed record UnavailabilityRequest(int Version, DateOnly FirstNight, DateOnly LastNight, string? Comments);
