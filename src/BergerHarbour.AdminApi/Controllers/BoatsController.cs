using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Boats.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController : ControllerBase
{
    private readonly BoatService service;

    public BoatsController(BoatService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<AdminBoat>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpGet("{id}")]
    public Task<AdminBoat> Get(string id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    public Task<AdminBoat> Create(SaveBoatRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<AdminBoat> Update(string id, SaveBoatRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
}
