using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Settings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings/blocked-periods")]
public sealed class BlockedPeriodsController : ControllerBase
{
    private readonly BlockedPeriodService service;

    public BlockedPeriodsController(BlockedPeriodService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<BlockedPeriodDto>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPost]
    public Task<BlockedPeriodDto> Create(SaveBlockedPeriodRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<BlockedPeriodDto> Update(string id, SaveBlockedPeriodRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
