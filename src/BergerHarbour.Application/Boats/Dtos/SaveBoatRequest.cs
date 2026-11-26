namespace BergerHarbour.Application.Boats.Dtos;

/// <summary><c>POST/PUT /api/boats</c>: every editable field (version 0 when creating).</summary>
public sealed record SaveBoatRequest(
    string Name,
    string Slug,
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
    IReadOnlyList<BoatRateDto>? Rates,
    IReadOnlyList<string>? AllowedAddonIds,
    int Version);
