using BergerHarbour.Application.Bookings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>Every active house boat and the booking settings. Also wakes the service on page load.</summary>
    [HttpGet]
    public Task<PublicFleetDto> List(CancellationToken ct) => bookings.GetFleetAsync(ct);

    /// <summary>Customer-safe data for one house boat, active or not. 404 for an unknown slug or a non-house boat.</summary>
    [HttpGet("{slug}")]
    public Task<PublicBoatDto> Get(string slug, CancellationToken ct) => bookings.GetBoatAsync(slug, ct);

    /// <summary>Prices a stay and runs the online rules (except the draft, terms and acceptance).</summary>
    [HttpPost("{slug}/quote")]
    public Task<QuoteDto> Quote(string slug, QuoteRequest request, CancellationToken ct) =>
        bookings.QuoteAsync(slug, request, ct);
}
