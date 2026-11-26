namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>
/// One boat's More details on the Dates &amp; boat step (§11.2): its photos, bedding, bond, effective deposit and rates (Midweek,
/// Weekend, Week and LongWeekend).
/// </summary>
public sealed record FleetBoatDetails(
    string Slug,
    IReadOnlyList<BoatImageDto> Images,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal DepositAmount,
    IReadOnlyList<BoatRateDto> Rates);
