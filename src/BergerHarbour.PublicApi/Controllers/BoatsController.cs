using BergerHarbour.Application.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>Customer-safe boat data and booking settings. Also wakes the service on page load.</summary>
    [HttpGet("{slug}")]
    public Task<PublicBoatDto> Get(string slug, CancellationToken ct) => bookings.GetBoatAsync(slug, ct);

    /// <summary>Unavailable ranges and enquire-only (blocked) periods. Never booking or customer data.</summary>
    [HttpGet("{slug}/availability")]
    public Task<AvailabilityDto> Availability(string slug, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct) => bookings.GetAvailabilityAsync(slug, from, to, ct);

    /// <summary>Prices a stay and runs the online rules (except Turnstile, terms and acceptance).</summary>
    [HttpPost("{slug}/quote")]
    public Task<QuoteDto> Quote(string slug, QuoteRequest request, CancellationToken ct) =>
        bookings.QuoteAsync(slug, request, ct);
}
