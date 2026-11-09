using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

public sealed record BusinessSettingsDto(
    decimal DepositAmount,
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string? StaffAlertEmail,
    string? ContactPhone,
    string? ContactEmail,
    string HireTermsUrl,
    string? AccountName,
    string? Bsb,
    string? AccountNumber,
    int Version);

public sealed record SeasonRangeDto(string Start, string End);
public sealed record SeasonDto(string Id, string Name, bool IsDefault, IReadOnlyList<SeasonRangeDto> Ranges, int Version);
public sealed record SaveSeasonRequest(string Name, IReadOnlyList<SeasonRangeDto>? Ranges, int Version);

public sealed record BlockedPeriodDto(string Id, string Name, DateOnly FirstNight, DateOnly LastNight, bool UsesExtendedPaymentSchedule, int Version);
public sealed record SaveBlockedPeriodRequest(string Name, DateOnly FirstNight, DateOnly LastNight, bool UsesExtendedPaymentSchedule, int Version);

public sealed record AddonDto(
    string Id,
    string Name,
    string? Description,
    bool PriceOnRequest,
    decimal? MidweekPrice,
    decimal? WeekendPrice,
    decimal? WeekPrice,
    bool QuantityApplies,
    int? Capacity,
    string? CapacityUnitLabel,
    bool IsActive,
    int Version);

public sealed record SaveAddonRequest(
    string Name,
    string? Description,
    bool PriceOnRequest,
    decimal? MidweekPrice,
    decimal? WeekendPrice,
    decimal? WeekPrice,
    bool QuantityApplies,
    int? Capacity,
    string? CapacityUnitLabel,
    bool IsActive,
    int Version);

public sealed record AddonOrderRequest(IReadOnlyList<string> AddonIds);

public sealed record EmailTemplateDto(EmailTemplateKey Key, string Subject, string HtmlBody, int Version);
public sealed record SaveEmailTemplateRequest(string Subject, string HtmlBody, int Version);
public sealed record EmailTemplatesResponse(IReadOnlyList<EmailTemplateDto> Templates, IReadOnlyList<string> Placeholders);

public sealed class SettingsService(
    IBusinessSettingsRepository business,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons,
    IEmailTemplateRepository templates,
    ReferenceDataLoader referenceData,
    BookingNotifier notifier,
    IClock clock)
{
    // ----- General -----

    public async Task<BusinessSettingsDto> GetBusinessAsync(CancellationToken ct = default) => ToDto(await referenceData.GetSettingsAsync(ct));

    public async Task<BusinessSettingsDto> UpdateBusinessAsync(BusinessSettingsDto r, CancellationToken ct = default)
    {
        var details = new BusinessSettingsDetails(r.DepositAmount, r.MinimumLeadTimeDays, r.BookingsOpenUntil, r.StaffAlertEmail,
            r.ContactPhone, r.ContactEmail, r.HireTermsUrl, r.AccountName, r.Bsb, r.AccountNumber);
        var s = await business.GetAsync(ct);
        if (s is null) s = BusinessSettings.Create(details, clock.UtcNow);
        else
        {
            s.EnsureVersion(r.Version);
            s.Update(details);
        }
        await business.SaveAsync(s, ct);
        return ToDto(s);
    }

    private static BusinessSettingsDto ToDto(BusinessSettings s) => new(s.DepositAmount, s.MinimumLeadTimeDays, s.BookingsOpenUntil,
        s.StaffAlertEmail, s.ContactPhone, s.ContactEmail, s.HireTermsUrl, s.AccountName, s.Bsb, s.AccountNumber, s.Version);

    // ----- Seasons -----

    public async Task<IReadOnlyList<SeasonDto>> ListSeasonsAsync(CancellationToken ct = default) =>
        (await referenceData.LoadAsync(ct)).Seasons.OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).Select(ToDto).ToList();

    private static SeasonDto ToDto(Season s) =>
        new(s.Id, s.Name, s.IsDefault, s.Ranges.Select(r => new SeasonRangeDto(r.Start.ToString(), r.End.ToString())).ToList(), s.Version);

    private static List<SeasonRange> ParseRanges(IReadOnlyList<SeasonRangeDto>? ranges)
    {
        var errors = new ValidationErrors();
        var list = new List<SeasonRange>();
        foreach (var r in ranges ?? [])
        {
            var start = errors.Capture(() => (DayMonth?)DayMonth.Parse(r.Start, "ranges"));
            var end = errors.Capture(() => (DayMonth?)DayMonth.Parse(r.End, "ranges"));
            if (start is { } s && end is { } e) list.Add(new SeasonRange(s, e));
        }
        errors.ThrowIfAny();
        return list;
    }

    public async Task<SeasonDto> CreateSeasonAsync(SaveSeasonRequest r, CancellationToken ct = default)
    {
        var all = await seasons.ListAsync(ct);
        var season = Season.Create(r.Name, ParseRanges(r.Ranges), all, clock.UtcNow);
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task<SeasonDto> UpdateSeasonAsync(string id, SaveSeasonRequest r, CancellationToken ct = default)
    {
        var all = await seasons.ListAsync(ct);
        var season = all.FirstOrDefault(s => s.Id == id) ?? throw new NotFoundException("Season not found.");
        season.EnsureVersion(r.Version);
        season.Update(r.Name, ParseRanges(r.Ranges), all);
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task DeleteSeasonAsync(string id, CancellationToken ct = default)
    {
        var season = await seasons.GetAsync(id, ct) ?? throw new NotFoundException("Season not found.");
        season.EnsureCanDelete();
        await seasons.DeleteAsync(season, ct);
    }

    // ----- Blocked periods -----

    public async Task<IReadOnlyList<BlockedPeriodDto>> ListBlockedPeriodsAsync(CancellationToken ct = default) =>
        (await blockedPeriods.ListAsync(ct)).OrderBy(p => p.Nights.FirstNight).Select(ToDto).ToList();

    private static BlockedPeriodDto ToDto(BlockedPeriod p) =>
        new(p.Id, p.Name, p.Nights.FirstNight, p.Nights.LastNight, p.UsesExtendedPaymentSchedule, p.Version);

    public async Task<BlockedPeriodDto> CreateBlockedPeriodAsync(SaveBlockedPeriodRequest r, CancellationToken ct = default)
    {
        var p = BlockedPeriod.Create(r.Name, r.FirstNight, r.LastNight, r.UsesExtendedPaymentSchedule, clock.UtcNow);
        await blockedPeriods.SaveAsync(p, ct);
        return ToDto(p);
    }

    public async Task<BlockedPeriodDto> UpdateBlockedPeriodAsync(string id, SaveBlockedPeriodRequest r, CancellationToken ct = default)
    {
        var p = await blockedPeriods.GetAsync(id, ct) ?? throw new NotFoundException("Blocked period not found.");
        p.EnsureVersion(r.Version);
        p.Update(r.Name, r.FirstNight, r.LastNight, r.UsesExtendedPaymentSchedule);
        await blockedPeriods.SaveAsync(p, ct);
        return ToDto(p);
    }

    public async Task DeleteBlockedPeriodAsync(string id, CancellationToken ct = default)
    {
        var p = await blockedPeriods.GetAsync(id, ct) ?? throw new NotFoundException("Blocked period not found.");
        await blockedPeriods.DeleteAsync(p, ct);
    }

    // ----- Add-ons -----

    public async Task<IReadOnlyList<AddonDto>> ListAddonsAsync(CancellationToken ct = default) =>
        (await addons.ListAsync(ct)).OrderBy(a => a.SortOrder).Select(ToDto).ToList();

    private static AddonDto ToDto(AddonDefinition a) => new(a.Id, a.Name, a.Description, a.PriceOnRequest, a.Prices?.Midweek,
        a.Prices?.Weekend, a.Prices?.Week, a.QuantityApplies, a.Capacity, a.CapacityUnitLabel, a.IsActive, a.Version);

    private static AddonDetails Details(SaveAddonRequest r) => new(r.Name, r.Description, r.PriceOnRequest, r.MidweekPrice,
        r.WeekendPrice, r.WeekPrice, r.QuantityApplies, r.Capacity, r.CapacityUnitLabel);

    public async Task<AddonDto> CreateAddonAsync(SaveAddonRequest r, CancellationToken ct = default)
    {
        var all = await addons.ListAsync(ct);
        var addon = AddonDefinition.Create(Details(r), all.Count == 0 ? 0 : all.Max(a => a.SortOrder) + 1, clock.UtcNow);
        if (!r.IsActive) addon.Deactivate();
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    public async Task<AddonDto> UpdateAddonAsync(string id, SaveAddonRequest r, CancellationToken ct = default)
    {
        var addon = await addons.GetAsync(id, ct) ?? throw new NotFoundException("Add-on not found.");
        addon.EnsureVersion(r.Version);
        addon.Update(Details(r));
        if (r.IsActive) addon.Activate(); else addon.Deactivate();
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    /// <summary>Delete = deactivate. The add-on keeps its place.</summary>
    public async Task<AddonDto> DeactivateAddonAsync(string id, CancellationToken ct = default)
    {
        var addon = await addons.GetAsync(id, ct) ?? throw new NotFoundException("Add-on not found.");
        addon.Deactivate();
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    /// <summary>Rewrites SortOrder from the full ordered list of ids.</summary>
    public async Task<IReadOnlyList<AddonDto>> ReorderAddonsAsync(AddonOrderRequest r, CancellationToken ct = default)
    {
        var all = await addons.ListAsync(ct);
        var ids = r.AddonIds ?? [];
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || !all.All(a => ids.Contains(a.Id)))
            throw new DomainValidationException("addonIds", "Send every add-on id exactly once.");
        var changed = new List<AddonDefinition>();
        for (var i = 0; i < ids.Count; i++)
        {
            var addon = all.First(a => a.Id == ids[i]);
            if (addon.SortOrder == i) continue;
            addon.MoveTo(i);
            changed.Add(addon);
        }
        await addons.SaveAllAsync(changed, ct);
        return all.OrderBy(a => a.SortOrder).Select(ToDto).ToList();
    }

    // ----- Email templates -----

    public async Task<EmailTemplatesResponse> ListTemplatesAsync(CancellationToken ct = default)
    {
        var stored = await templates.ListAsync(ct);
        var list = Enum.GetValues<EmailTemplateKey>().Select(k =>
        {
            var t = stored.FirstOrDefault(x => x.Key == k);
            var (subject, body) = t is null ? DefaultEmailTemplates.All[k] : (t.Subject, t.HtmlBody);
            return new EmailTemplateDto(k, subject, body, t?.Version ?? 0);
        }).ToList();
        return new EmailTemplatesResponse(list, EmailPlaceholders.All);
    }

    public async Task<EmailTemplateDto> UpdateTemplateAsync(EmailTemplateKey key, SaveEmailTemplateRequest r, CancellationToken ct = default)
    {
        var t = await templates.GetAsync(key, ct);
        if (t is null)
        {
            if (r.Version != 0) throw new ConcurrencyConflictException();
            t = EmailTemplate.Create(key, r.Subject, r.HtmlBody, clock.UtcNow);
        }
        else
        {
            t.EnsureVersion(r.Version);
            t.Update(r.Subject, r.HtmlBody);
        }
        await templates.SaveAsync(t, ct);
        return new EmailTemplateDto(t.Key, t.Subject, t.HtmlBody, t.Version);
    }

    public Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default) => notifier.SendTestAsync(key, ct);
}
