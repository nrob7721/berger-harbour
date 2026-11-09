namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>GET /api/availability</c>: ranges only.</summary>
public sealed record FleetAvailabilityResponse(
    IReadOnlyList<EnquireOnlyRange> EnquireOnly,
    IReadOnlyList<BoatAvailability> Boats);
