using BergerHarbour.Application.Settings;
using BergerHarbour.Domain.Settings;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet("business")]
    public Task<BusinessSettingsDto> GetBusiness(CancellationToken ct) => settings.GetBusinessAsync(ct);

    [HttpPut("business")]
    public Task<BusinessSettingsDto> UpdateBusiness(BusinessSettingsDto request, CancellationToken ct) =>
        settings.UpdateBusinessAsync(request, ct);

    [HttpGet("seasons")]
    public Task<IReadOnlyList<SeasonDto>> Seasons(CancellationToken ct) => settings.ListSeasonsAsync(ct);

    [HttpPost("seasons")]
    public Task<SeasonDto> CreateSeason(SeasonRequest request, CancellationToken ct) => settings.CreateSeasonAsync(request, ct);

    [HttpPut("seasons/{id}")]
    public Task<SeasonDto> UpdateSeason(string id, SeasonRequest request, CancellationToken ct) =>
        settings.UpdateSeasonAsync(id, request, ct);

    [HttpDelete("seasons/{id}")]
    public async Task<IActionResult> DeleteSeason(string id, [FromQuery] int version, CancellationToken ct)
    {
        await settings.DeleteSeasonAsync(id, version, ct);
        return NoContent();
    }

    [HttpGet("blocked-periods")]
    public Task<IReadOnlyList<BlockedPeriodDto>> BlockedPeriods(CancellationToken ct) => settings.ListBlockedPeriodsAsync(ct);

    [HttpPost("blocked-periods")]
    public Task<BlockedPeriodDto> CreateBlockedPeriod(BlockedPeriodRequest request, CancellationToken ct) =>
        settings.CreateBlockedPeriodAsync(request, ct);

    [HttpPut("blocked-periods/{id}")]
    public Task<BlockedPeriodDto> UpdateBlockedPeriod(string id, BlockedPeriodRequest request, CancellationToken ct) =>
        settings.UpdateBlockedPeriodAsync(id, request, ct);

    [HttpDelete("blocked-periods/{id}")]
    public async Task<IActionResult> DeleteBlockedPeriod(string id, [FromQuery] int version, CancellationToken ct)
    {
        await settings.DeleteBlockedPeriodAsync(id, version, ct);
        return NoContent();
    }

    [HttpGet("addons")]
    public Task<IReadOnlyList<AddonDto>> Addons(CancellationToken ct) => settings.ListAddonsAsync(ct);

    [HttpPost("addons")]
    public Task<AddonDto> CreateAddon(AddonRequest request, CancellationToken ct) => settings.CreateAddonAsync(request, ct);

    [HttpPut("addons/{id}")]
    public Task<AddonDto> UpdateAddon(string id, AddonRequest request, CancellationToken ct) =>
        settings.UpdateAddonAsync(id, request, ct);

    /// <summary>Delete = deactivate.</summary>
    [HttpDelete("addons/{id}")]
    public async Task<IActionResult> DeactivateAddon(string id, [FromQuery] int version, CancellationToken ct)
    {
        await settings.DeactivateAddonAsync(id, version, ct);
        return NoContent();
    }

    [HttpGet("email-templates")]
    public Task<EmailTemplatesDto> Templates(CancellationToken ct) => settings.ListTemplatesAsync(ct);

    [HttpPut("email-templates/{key}")]
    public Task<EmailTemplateDto> UpdateTemplate(EmailTemplateKey key, EmailTemplateRequest request, CancellationToken ct) =>
        settings.UpdateTemplateAsync(key, request, ct);

    /// <summary>Sends a sample of the template to the staff alert address.</summary>
    [HttpPost("email-templates/{key}/test")]
    public async Task<IActionResult> SendTest(EmailTemplateKey key, CancellationToken ct)
    {
        await settings.SendTestAsync(key, ct);
        return NoContent();
    }
}
