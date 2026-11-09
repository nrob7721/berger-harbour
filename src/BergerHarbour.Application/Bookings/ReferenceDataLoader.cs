using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings;

/// <summary>Loads <see cref="ReferenceData"/>.</summary>
public sealed class ReferenceDataLoader
{
    private readonly IBusinessSettingsRepository settings;
    private readonly ISeasonRepository seasons;
    private readonly IBlockedPeriodRepository blockedPeriods;
    private readonly IAddonDefinitionRepository addons;

    public ReferenceDataLoader(
        IBusinessSettingsRepository settings, ISeasonRepository seasons, IBlockedPeriodRepository blockedPeriods, IAddonDefinitionRepository addons)
    {
        this.settings = settings;
        this.seasons = seasons;
        this.blockedPeriods = blockedPeriods;
        this.addons = addons;
    }

    public async Task<BusinessSettings> SettingsAsync(CancellationToken ct = default) =>
        await settings.GetAsync(ct) ?? throw new InvalidOperationException("Business settings are missing. Run the seed first.");

    public async Task<ReferenceData> LoadAsync(CancellationToken ct = default)
    {
        var settingsTask = SettingsAsync(ct);
        var seasonsTask = seasons.ListAsync(ct);
        var blockedTask = blockedPeriods.ListAsync(ct);
        var addonsTask = addons.ListAsync(ct);
        await Task.WhenAll(settingsTask, seasonsTask, blockedTask, addonsTask);
        return new ReferenceData(await settingsTask, await seasonsTask, await blockedTask, await addonsTask);
    }
}
