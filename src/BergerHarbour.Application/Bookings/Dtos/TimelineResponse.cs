namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>GET /api/bookings</c> (admin): timeline data.</summary>
public sealed record TimelineResponse(
    IReadOnlyList<TimelineBoat> Boats,
    IReadOnlyList<TimelineBooking> Bookings,
    IReadOnlyList<TimelineUnavailability> Unavailabilities,
    IReadOnlyList<TimelineBlockedPeriod> BlockedPeriods);
