using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>A boat in Fleet, with what is missing before it can be activated.</summary>
public sealed record AdminBoat(
    string Id,
    string Name,
    string Slug,
    BoatType Type,
    int Berth,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal? DepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly,
    bool IsActive,
    IReadOnlyList<BoatRateDto> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    IReadOnlyList<string> MissingForActivation,
    int Version);
