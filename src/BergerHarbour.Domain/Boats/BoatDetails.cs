using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Domain.Boats;

/// <summary>The editable fields of a boat, validated by <see cref="Boat"/>.</summary>
public sealed record BoatDetails(
    string? Name,
    string? Slug,
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
    IReadOnlyList<BoatRate> Rates,
    IReadOnlyList<AddonId> AllowedAddonIds);
