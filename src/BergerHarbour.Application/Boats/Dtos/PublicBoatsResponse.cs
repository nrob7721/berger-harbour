namespace BergerHarbour.Application.Boats.Dtos;

/// <summary><c>GET /api/boats</c>: every active house boat in boat order (§7.3) and the booking settings.</summary>
public sealed record PublicBoatsResponse(
    IReadOnlyList<PublicBoatSummary> Boats,
    PublicBookingSettings Settings);
