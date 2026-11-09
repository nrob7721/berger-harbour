using BergerHarbour.Application.Unavailabilities;
using BergerHarbour.Application.Unavailabilities.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/boats/{id}/unavailabilities")]
public sealed class BoatUnavailabilitiesController : ControllerBase
{
    private readonly BoatUnavailabilityService service;

    public BoatUnavailabilitiesController(BoatUnavailabilityService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<UnavailabilityDto>> List(string id, CancellationToken ct) => service.ListAsync(id, ct);

    [HttpPost]
    public Task<UnavailabilityDto> Create(string id, SaveUnavailabilityRequest request, CancellationToken ct) => service.CreateAsync(id, request, ct);

    [HttpPut("{uid}")]
    public Task<UnavailabilityDto> Update(string id, string uid, SaveUnavailabilityRequest request, CancellationToken ct) =>
        service.UpdateAsync(id, uid, request, ct);

    [HttpDelete("{uid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id, string uid, CancellationToken ct)
    {
        await service.DeleteAsync(id, uid, ct);
        return NoContent();
    }
}
