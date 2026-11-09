using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Settings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings/seasons")]
public sealed class SeasonsController : ControllerBase
{
    private readonly SeasonService service;

    public SeasonsController(SeasonService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<SeasonDto>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPost]
    public Task<SeasonDto> Create(SaveSeasonRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<SeasonDto> Update(string id, SaveSeasonRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
