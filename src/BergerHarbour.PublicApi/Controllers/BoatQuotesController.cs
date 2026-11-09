using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boats/{slug}/quote")]
public sealed class BoatQuotesController : ControllerBase
{
    private readonly OnlineQuoteService service;

    public BoatQuotesController(OnlineQuoteService service)
    {
        this.service = service;
    }

    [HttpPost]
    public Task<QuoteResponse> Quote(string slug, QuoteRequest request, CancellationToken ct) => service.QuoteAsync(slug, request, ct);
}
