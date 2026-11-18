using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>A boat in Fleet, with what is missing before it can be activated. <c>EffectiveDepositAmount</c> is its own deposit, or the Settings default.</summary>
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
    decimal EffectiveDepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly,
    bool IsActive,
    IReadOnlyList<BoatRateDto> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    IReadOnlyList<string> MissingForActivation,
    int Version);
