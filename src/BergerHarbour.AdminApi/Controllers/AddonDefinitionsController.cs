using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Settings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings/addons")]
public sealed class AddonDefinitionsController : ControllerBase
{
    private readonly AddonDefinitionService service;

    public AddonDefinitionsController(AddonDefinitionService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<AddonDto>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPost]
    public Task<AddonDto> Create(SaveAddonRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    /// <summary>The full ordered list of add-on ids; the server rewrites the sort order.</summary>
    [HttpPut("order")]
    public Task<IReadOnlyList<AddonDto>> Reorder(AddonOrderRequest request, CancellationToken ct) => service.ReorderAsync(request, ct);

    [HttpPut("{id}")]
    public Task<AddonDto> Update(string id, SaveAddonRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    /// <summary>Delete = deactivate.</summary>
    [HttpDelete("{id}")]
    public Task<AddonDto> Deactivate(string id, CancellationToken ct) => service.DeactivateAsync(id, ct);
}
