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
    public Task<BusinessSettingsDto> UpdateBusiness(BusinessSettingsDto request, CancellationToken ct) => settings.UpdateBusinessAsync(request, ct);

    // Seasons
    [HttpGet("seasons")]
    public Task<IReadOnlyList<SeasonDto>> Seasons(CancellationToken ct) => settings.ListSeasonsAsync(ct);

    [HttpPost("seasons")]
    public Task<SeasonDto> CreateSeason(SaveSeasonRequest request, CancellationToken ct) => settings.CreateSeasonAsync(request, ct);

    [HttpPut("seasons/{id}")]
    public Task<SeasonDto> UpdateSeason(string id, SaveSeasonRequest request, CancellationToken ct) => settings.UpdateSeasonAsync(id, request, ct);

    [HttpDelete("seasons/{id}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSeason(string id, CancellationToken ct)
    {
        await settings.DeleteSeasonAsync(id, ct);
        return NoContent();
    }

    // Blocked periods
    [HttpGet("blocked-periods")]
    public Task<IReadOnlyList<BlockedPeriodDto>> BlockedPeriods(CancellationToken ct) => settings.ListBlockedPeriodsAsync(ct);

    [HttpPost("blocked-periods")]
    public Task<BlockedPeriodDto> CreateBlockedPeriod(SaveBlockedPeriodRequest request, CancellationToken ct) =>
        settings.CreateBlockedPeriodAsync(request, ct);

    [HttpPut("blocked-periods/{id}")]
    public Task<BlockedPeriodDto> UpdateBlockedPeriod(string id, SaveBlockedPeriodRequest request, CancellationToken ct) =>
        settings.UpdateBlockedPeriodAsync(id, request, ct);

    [HttpDelete("blocked-periods/{id}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteBlockedPeriod(string id, CancellationToken ct)
    {
        await settings.DeleteBlockedPeriodAsync(id, ct);
        return NoContent();
    }

    // Add-ons
    [HttpGet("addons")]
    public Task<IReadOnlyList<AddonDto>> Addons(CancellationToken ct) => settings.ListAddonsAsync(ct);

    [HttpPost("addons")]
    public Task<AddonDto> CreateAddon(SaveAddonRequest request, CancellationToken ct) => settings.CreateAddonAsync(request, ct);

    [HttpPut("addons/order")]
    public Task<IReadOnlyList<AddonDto>> ReorderAddons(AddonOrderRequest request, CancellationToken ct) => settings.ReorderAddonsAsync(request, ct);

    [HttpPut("addons/{id}")]
    public Task<AddonDto> UpdateAddon(string id, SaveAddonRequest request, CancellationToken ct) => settings.UpdateAddonAsync(id, request, ct);

    /// <summary>Delete = deactivate.</summary>
    [HttpDelete("addons/{id}")]
    public Task<AddonDto> DeactivateAddon(string id, CancellationToken ct) => settings.DeactivateAddonAsync(id, ct);

    // Email templates
    [HttpGet("email-templates")]
    public Task<EmailTemplatesResponse> Templates(CancellationToken ct) => settings.ListTemplatesAsync(ct);

    [HttpPut("email-templates/{key}")]
    public Task<EmailTemplateDto> UpdateTemplate(EmailTemplateKey key, SaveEmailTemplateRequest request, CancellationToken ct) =>
        settings.UpdateTemplateAsync(key, request, ct);

    /// <summary>Sends the saved template with sample data to the staff alert address.</summary>
    [HttpPost("email-templates/{key}/test")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SendTest(EmailTemplateKey key, CancellationToken ct)
    {
        await settings.SendTestAsync(key, ct);
        return NoContent();
    }
}
