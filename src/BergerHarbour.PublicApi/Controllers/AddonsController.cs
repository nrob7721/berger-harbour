using BergerHarbour.Application.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/addons")]
public sealed class AddonsController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>What is left of a capacity-limited add-on for the dates, across all boats. 404 without a capacity.</summary>
    [HttpGet("{addonId}/availability")]
    public Task<PublicAddonAvailabilityDto> Availability(string addonId, [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate, CancellationToken ct) =>
        bookings.GetAddonAvailabilityAsync(addonId, startDate, endDate, ct);
}
