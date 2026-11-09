using BergerHarbour.Application.Catalogue;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController(PublicCatalogueService catalogue, Application.Bookings.OnlineBookingService booking) : ControllerBase
{
    /// <summary>Every active house boat (berth desc, then name) and the booking settings. Also the warm-up call.</summary>
    [HttpGet]
    public Task<PublicBoatsResponse> List(CancellationToken ct) => catalogue.GetBoatsAsync(ct);

    /// <summary>Customer-safe data for one boat, active or not.</summary>
    [HttpGet("{slug}")]
    public Task<PublicBoatDetails> Get(string slug, CancellationToken ct) => catalogue.GetBoatAsync(slug, ct);

    /// <summary>Server-side quote for a stay on this boat.</summary>
    [HttpPost("{slug}/quote")]
    public Task<QuoteResponse> Quote(string slug, QuoteRequest request, CancellationToken ct) => booking.QuoteAsync(slug, request, ct);
}

[ApiController]
[Route("api/availability")]
public sealed class AvailabilityController(PublicCatalogueService catalogue) : ControllerBase
{
    /// <summary>Fleet availability as ranges only (never booking or customer data). At most 15 months.</summary>
    [HttpGet]
    public Task<FleetAvailabilityResponse> Fleet([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        catalogue.GetAvailabilityAsync(from, to, ct);

    /// <summary>Each active house boat's availability and hire price for one online stay.</summary>
    [HttpGet("period")]
    public Task<PeriodAvailabilityResponse> Period([FromQuery] PeriodType periodType, [FromQuery] DateOnly startDate, CancellationToken ct) =>
        catalogue.GetPeriodAvailabilityAsync(periodType, startDate, ct);
}

[ApiController]
[Route("api/addons")]
public sealed class AddonsController(PublicCatalogueService catalogue) : ControllerBase
{
    /// <summary>Remaining capacity of a capacity-limited add-on across all boats.</summary>
    [HttpGet("{addonId}/availability")]
    public Task<AddonRemainingResponse> Availability(string addonId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken ct) =>
        catalogue.GetAddonRemainingAsync(addonId, startDate, endDate, null, ct);
}
