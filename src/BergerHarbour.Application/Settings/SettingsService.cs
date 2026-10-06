using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>Staff use cases for the Settings page.</summary>
public sealed class SettingsService(
    IBusinessSettingsRepository business,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons,
    IEmailTemplateRepository templates,
    NotificationService notifications)
{
    // ---- General --------------------------------------------------------------------------------------------------

    public async Task<BusinessSettingsDto> GetBusinessAsync(CancellationToken ct = default) => ToDto(await business.GetAsync(ct));

    public async Task<BusinessSettingsDto> UpdateBusinessAsync(BusinessSettingsDto request, CancellationToken ct = default)
    {
        var settings = await business.GetAsync(ct);
        settings.EnsureVersion(request.Version);
        settings.Update(request.DepositAmount, request.MinimumLeadTimeDays, request.BookingsOpenUntil, request.StaffAlertEmail,
            request.ContactPhone, request.ContactEmail, request.HireTermsUrl);
        await business.SaveAsync(settings, ct);
        return ToDto(settings);
    }

    // ---- Seasons --------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SeasonDto>> ListSeasonsAsync(CancellationToken ct = default) =>
        (await seasons.ListAsync(ct)).OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).Select(ToDto).ToList();

    public async Task<SeasonDto> CreateSeasonAsync(SeasonRequest request, CancellationToken ct = default)
    {
        var season = Season.Create(request.Name, Ranges(request));
        await EnsureSeasonIsValidAsync(season, ct);
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task<SeasonDto> UpdateSeasonAsync(string id, SeasonRequest request, CancellationToken ct = default)
    {
        var season = await seasons.GetAsync(new SeasonId(id), ct) ?? throw new NotFoundException("Season not found.");
        season.EnsureVersion(request.Version);
        season.Update(request.Name, Ranges(request));
        await EnsureSeasonIsValidAsync(season, ct);
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task DeleteSeasonAsync(string id, int version, CancellationToken ct = default)
    {
        var season = await seasons.GetAsync(new SeasonId(id), ct) ?? throw new NotFoundException("Season not found.");
        season.EnsureVersion(version);
        season.EnsureCanBeDeleted();
        await seasons.DeleteAsync(season, ct);
    }

    private async Task EnsureSeasonIsValidAsync(Season season, CancellationToken ct)
    {
        var all = await seasons.ListAsync(ct);
        if (all.Any(s => s.Id != season.Id && string.Equals(s.Name, season.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw RequestValidationException.For("name", $"There is already a season called {season.Name}.");
        }

        SeasonCatalogue.EnsureNoOverlap(season, all);
    }

    private static IEnumerable<DayMonthRange> Ranges(SeasonRequest request) =>
        (request.Ranges ?? []).Select(r => new DayMonthRange(DayMonth.Parse(r.StartDayMonth), DayMonth.Parse(r.EndDayMonth))).ToList();

    // ---- Blocked periods ------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BlockedPeriodDto>> ListBlockedPeriodsAsync(CancellationToken ct = default) =>
        (await blockedPeriods.ListAsync(ct)).OrderBy(p => p.Nights.FirstNight).Select(ToDto).ToList();

    public async Task<BlockedPeriodDto> CreateBlockedPeriodAsync(BlockedPeriodRequest request, CancellationToken ct = default)
    {
        var period = BlockedPeriod.Create(request.Name, request.FirstNight, request.LastNight, request.UsesExtendedPaymentSchedule);
        await blockedPeriods.SaveAsync(period, ct);
        return ToDto(period);
    }

    /// <summary>
    /// Changing a blocked period does not re-derive the payment schedule of existing bookings; staff re-save a
    /// booking to re-derive it.
    /// </summary>
    public async Task<BlockedPeriodDto> UpdateBlockedPeriodAsync(string id, BlockedPeriodRequest request, CancellationToken ct = default)
    {
        var period = await blockedPeriods.GetAsync(new BlockedPeriodId(id), ct) ?? throw new NotFoundException("Blocked period not found.");
        period.EnsureVersion(request.Version);
        period.Update(request.Name, request.FirstNight, request.LastNight, request.UsesExtendedPaymentSchedule);
        await blockedPeriods.SaveAsync(period, ct);
        return ToDto(period);
    }

    public async Task DeleteBlockedPeriodAsync(string id, int version, CancellationToken ct = default)
    {
        var period = await blockedPeriods.GetAsync(new BlockedPeriodId(id), ct) ?? throw new NotFoundException("Blocked period not found.");
        period.EnsureVersion(version);
        await blockedPeriods.DeleteAsync(period, ct);
    }

    // ---- Add-ons --------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AddonDto>> ListAddonsAsync(CancellationToken ct = default) =>
        (await addons.ListAsync(ct)).OrderBy(a => a.Name).Select(ToDto).ToList();

    public async Task<AddonDto> CreateAddonAsync(AddonRequest request, CancellationToken ct = default)
    {
        var addon = AddonDefinition.Create(request.Name, request.Description, request.PriceOnRequest, Prices(request),
            request.QuantityApplies);
        if (!request.IsActive)
        {
            addon.Deactivate();
        }

        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    public async Task<AddonDto> UpdateAddonAsync(string id, AddonRequest request, CancellationToken ct = default)
    {
        var addon = await addons.GetAsync(new AddonId(id), ct) ?? throw new NotFoundException("Add-on not found.");
        addon.EnsureVersion(request.Version);
        addon.Update(request.Name, request.Description, request.PriceOnRequest, Prices(request), request.QuantityApplies);
        if (request.IsActive)
        {
            addon.Activate();
        }
        else
        {
            addon.Deactivate();
        }

        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    /// <summary>Delete = deactivate, so existing booking lines keep their reference.</summary>
    public async Task DeactivateAddonAsync(string id, int version, CancellationToken ct = default)
    {
        var addon = await addons.GetAsync(new AddonId(id), ct) ?? throw new NotFoundException("Add-on not found.");
        addon.EnsureVersion(version);
        addon.Deactivate();
        await addons.SaveAsync(addon, ct);
    }

    private static AddonPrices? Prices(AddonRequest request) =>
        request.PriceOnRequest || request.Prices is null
            ? null
            : new AddonPrices(request.Prices.Midweek, request.Prices.Weekend, request.Prices.Week);

    // ---- Email templates ------------------------------------------------------------------------------------------

    public async Task<EmailTemplatesDto> ListTemplatesAsync(CancellationToken ct = default) =>
        new((await templates.ListAsync(ct)).OrderBy(t => t.Key).Select(ToDto).ToList(), EmailPlaceholders.All);

    public async Task<EmailTemplateDto> UpdateTemplateAsync(EmailTemplateKey key, EmailTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await templates.GetAsync(key, ct) ?? throw new NotFoundException("Template not found.");
        template.EnsureVersion(request.Version);
        template.Update(request.Subject, request.HtmlBody);
        await templates.SaveAsync(template, ct);
        return ToDto(template);
    }

    public Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default) => notifications.SendTestAsync(key, ct);

    // ---- Mapping --------------------------------------------------------------------------------------------------

    private static BusinessSettingsDto ToDto(BusinessSettings s) => new(s.Version, s.DepositAmount, s.MinimumLeadTimeDays,
        s.BookingsOpenUntil, s.StaffAlertEmail, s.ContactPhone, s.ContactEmail, s.HireTermsUrl);

    private static SeasonDto ToDto(Season s) => new(s.Id.Value, s.Version, s.Name, s.IsDefault,
        s.Ranges.Select(r => new SeasonRangeDto(r.Start.ToString(), r.End.ToString())).ToList());

    private static BlockedPeriodDto ToDto(BlockedPeriod p) =>
        new(p.Id.Value, p.Version, p.Name, p.Nights.FirstNight, p.Nights.LastNight, p.UsesExtendedPaymentSchedule);

    private static AddonDto ToDto(AddonDefinition a) => new(a.Id.Value, a.Version, a.Name, a.Description, a.PriceOnRequest,
        a.Prices is { } p ? new AddonPricesDto(p.Midweek, p.Weekend, p.Week) : null, a.QuantityApplies, a.IsActive);

    private static EmailTemplateDto ToDto(EmailTemplate t) => new(t.Key, t.Version, t.Subject, t.HtmlBody);
}
