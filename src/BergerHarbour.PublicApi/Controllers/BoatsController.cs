using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Boats.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController : ControllerBase
{
    private readonly BoatCatalogueService service;

    public BoatsController(BoatCatalogueService service)
    {
        this.service = service;
    }

    /// <summary>Every active house boat and the booking settings. Also the warm-up call.</summary>
    [HttpGet]
    public Task<PublicBoatsResponse> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpGet("{slug}")]
    public Task<PublicBoatDetails> Get(string slug, CancellationToken ct) => service.GetAsync(slug, ct);
}
