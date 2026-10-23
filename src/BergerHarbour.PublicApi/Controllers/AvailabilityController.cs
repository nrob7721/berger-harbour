using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/availability")]
public sealed class AvailabilityController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>
    /// Unavailable ranges of every active house boat and the enquire-only (blocked) periods. Never booking or customer
    /// data.
    /// </summary>
    [HttpGet]
    public Task<FleetAvailabilityDto> Fleet([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        bookings.GetFleetAvailabilityAsync(from, to, ct);

    /// <summary>Each active house boat's availability and hire price for one stay. 400 when the stay is not bookable online.</summary>
    [HttpGet("period")]
    public Task<PeriodAvailabilityDto> Period([FromQuery] PeriodType periodType, [FromQuery] DateOnly startDate,
        CancellationToken ct) => bookings.GetPeriodAvailabilityAsync(periodType, startDate, ct);
}
