using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>GET /api/availability/period</c>: every active house boat for one stay.</summary>
public sealed record PeriodAvailabilityResponse(
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<PeriodBoatAvailability> Boats);
