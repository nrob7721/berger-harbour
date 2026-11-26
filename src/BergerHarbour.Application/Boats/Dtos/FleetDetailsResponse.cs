namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>
/// <c>GET /api/boat-details</c>: the More details of every active house boat, in boat order, and every season (Normal first, the
/// others by their earliest range). No booking or customer data.
/// </summary>
public sealed record FleetDetailsResponse(
    IReadOnlyList<FleetBoatDetails> Boats,
    IReadOnlyList<PublicSeason> Seasons);
