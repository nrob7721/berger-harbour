using BergerHarbour.Application.Boats;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController(FleetService fleet) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BoatSummaryDto>> List(CancellationToken ct) => fleet.ListAsync(ct);

    [HttpGet("{id}")]
    public Task<BoatDetailDto> Get(string id, CancellationToken ct) => fleet.GetAsync(id, ct);

    [HttpPost]
    public Task<BoatDetailDto> Create(BoatUpsertRequest request, CancellationToken ct) => fleet.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<BoatDetailDto> Update(string id, BoatUpsertRequest request, CancellationToken ct) =>
        fleet.UpdateAsync(id, request, ct);

    [HttpGet("{id}/unavailabilities")]
    public Task<IReadOnlyList<UnavailabilityDto>> Unavailabilities(string id, CancellationToken ct) =>
        fleet.ListUnavailabilitiesAsync(id, ct);

    [HttpPost("{id}/unavailabilities")]
    public Task<UnavailabilityDto> CreateUnavailability(string id, UnavailabilityRequest request, CancellationToken ct) =>
        fleet.CreateUnavailabilityAsync(id, request, ct);

    [HttpPut("{id}/unavailabilities/{uid}")]
    public Task<UnavailabilityDto> UpdateUnavailability(string id, string uid, UnavailabilityRequest request, CancellationToken ct) =>
        fleet.UpdateUnavailabilityAsync(id, uid, request, ct);

    [HttpDelete("{id}/unavailabilities/{uid}")]
    public async Task<IActionResult> DeleteUnavailability(string id, string uid, [FromQuery] int version, CancellationToken ct)
    {
        await fleet.DeleteUnavailabilityAsync(id, uid, version, ct);
        return NoContent();
    }
}
