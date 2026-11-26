namespace BergerHarbour.Application.Boats.Dtos;

/// <summary><c>GET /api/boats/{slug}</c>: customer-safe data for one boat, active or not.</summary>
public sealed record PublicBoatDetails(
    string Slug,
    string Name,
    string? ImageUrl,
    int Berth,
    bool HasSpa,
    bool IsPetFriendly,
    int AdultBeds,
    int KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal DepositAmount,
    bool IsActive,
    IReadOnlyList<PublicAddon> Addons);
