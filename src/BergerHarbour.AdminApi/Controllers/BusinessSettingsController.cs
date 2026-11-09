using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Settings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings/business")]
public sealed class BusinessSettingsController : ControllerBase
{
    private readonly BusinessSettingsService service;

    public BusinessSettingsController(BusinessSettingsService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<BusinessSettingsDto> Get(CancellationToken ct) => service.GetAsync(ct);

    [HttpPut]
    public Task<BusinessSettingsDto> Update(BusinessSettingsDto request, CancellationToken ct) => service.UpdateAsync(request, ct);
}
