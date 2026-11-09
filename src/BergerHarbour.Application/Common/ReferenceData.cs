using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Common;

/// <summary>The small, rarely-changing data most use cases need.</summary>
public sealed record ReferenceData(
    BusinessSettings Settings,
    IReadOnlyList<Season> Seasons,
    IReadOnlyList<BlockedPeriod> BlockedPeriods,
    IReadOnlyList<AddonDefinition> Addons)
{
    public AddonDefinition? Addon(string id) => Addons.FirstOrDefault(a => a.Id == id);
}

public sealed class ReferenceDataLoader(
    IBusinessSettingsRepository settings,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons,
    Abstractions.IClock clock)
{
    public async Task<ReferenceData> LoadAsync(CancellationToken ct = default)
    {
        var s = settings.GetAsync(ct);
        var se = seasons.ListAsync(ct);
        var bp = blockedPeriods.ListAsync(ct);
        var a = addons.ListAsync(ct);
        await Task.WhenAll(s, se, bp, a);
        var seasonList = se.Result.ToList();
        if (!seasonList.Any(x => x.IsDefault)) seasonList.Insert(0, Season.CreateNormal(clock.UtcNow));
        return new ReferenceData(s.Result ?? await GetSettingsAsync(ct), seasonList, bp.Result, a.Result);
    }

    public async Task<BusinessSettings> GetSettingsAsync(CancellationToken ct = default) =>
        await settings.GetAsync(ct) ?? BusinessSettings.Create(DefaultSettings(SydneyTime.Today(clock.UtcNow)), clock.UtcNow);

    public static BusinessSettingsDetails DefaultSettings(DateOnly today) =>
        new(1000m, 30, new DateOnly(Math.Max(2027, today.Year + 1), 11, 30), null, null, null, BusinessSettings.DefaultHireTermsUrl, null, null, null);
}

public static class BoatOrdering
{
    /// <summary>Berth descending, then name.</summary>
    public static IEnumerable<Boat> InDisplayOrder(this IEnumerable<Boat> boats) => Boat.InDisplayOrder(boats, b => b.Berth, b => b.Name);
}
