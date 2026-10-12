using BergerHarbour.Application.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/addons")]
public sealed class AddonsController(AddonCapacityQueries capacity) : ControllerBase
{
    /// <summary>Remaining capacity across all boats for the dates, optionally excluding the booking being edited.</summary>
    [HttpGet("{addonId}/availability")]
    public Task<AddonAvailabilityDto> Availability(string addonId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        [FromQuery] string? excludeBookingId, CancellationToken ct) =>
        capacity.RemainingAsync(addonId, startDate, endDate, excludeBookingId, ct);
}
