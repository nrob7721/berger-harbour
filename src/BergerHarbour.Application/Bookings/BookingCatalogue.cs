using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings;

/// <summary>The settings aggregates every booking calculation needs, loaded together.</summary>
public sealed record BookingCatalogue(
    BusinessSettings Settings,
    IReadOnlyList<Season> Seasons,
    IReadOnlyList<BlockedPeriod> BlockedPeriods,
    IReadOnlyList<AddonDefinition> Addons);

public sealed class BookingCatalogueLoader(
    IBusinessSettingsRepository settings,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons)
{
    public async Task<BookingCatalogue> LoadAsync(CancellationToken ct = default)
    {
        var settingsTask = settings.GetAsync(ct);
        var seasonsTask = seasons.ListAsync(ct);
        var blockedTask = blockedPeriods.ListAsync(ct);
        var addonsTask = addons.ListAsync(ct);
        await Task.WhenAll(settingsTask, seasonsTask, blockedTask, addonsTask);
        return new BookingCatalogue(settingsTask.Result, seasonsTask.Result, blockedTask.Result, addonsTask.Result);
    }
}
