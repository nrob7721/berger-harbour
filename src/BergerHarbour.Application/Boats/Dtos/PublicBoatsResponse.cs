namespace BergerHarbour.Application.Boats.Dtos;

/// <summary><c>GET /api/boats</c>: every active house boat (berth desc, then name) and the booking settings.</summary>
public sealed record PublicBoatsResponse(
    IReadOnlyList<PublicBoatSummary> Boats,
    PublicBookingSettings Settings);
