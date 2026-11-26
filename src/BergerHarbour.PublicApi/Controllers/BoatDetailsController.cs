using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Boats.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/boat-details")]
public sealed class BoatDetailsController : ControllerBase
{
    private readonly BoatDetailsService service;

    public BoatDetailsController(BoatDetailsService service)
    {
        this.service = service;
    }

    /// <summary>The More details (photos, bedding, bond, deposit, rates) of every active house boat, and every season.</summary>
    [HttpGet]
    public Task<FleetDetailsResponse> List(CancellationToken ct) => service.ListAsync(ct);
}
