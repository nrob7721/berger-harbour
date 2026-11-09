using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Seed;

public sealed record SeedOptions(string? StaffAlertEmail, string? ContactPhone, string? ContactEmail);

/// <summary>
/// One-off setup data. Idempotent: it only adds what is missing and never overwrites staff edits.
/// Boat data comes from each boat's page on bergerhouseboats.com.au; staff verify every boat in Fleet before activating it.
/// </summary>
public sealed class SeedService(
    IBusinessSettingsRepository settings,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons,
    IBoatRepository boats,
    IEmailTemplateRepository templates,
    IClock clock,
    ILogger<SeedService> logger)
{
    public const string OffPeakId = "off-peak";
    public const string PeakId = "peak";
    public const string ChristmasId = "christmas-new-year-2026-27";

    private sealed record SeasonRates(decimal Weekend, decimal Midweek, decimal LongWeekend, decimal Week);

    private sealed record SeedBoat(
        string Name, string Slug, int Berth, int AdultBeds, int KidsBeds, string Bedding, decimal Bond, decimal? Deposit,
        string ImageUrl, bool Spa, bool Pets, SeasonRates Normal, SeasonRates OffPeak, SeasonRates Peak, bool Active = false);

    private static readonly SeasonRates PbNormal = new(4060, 4060, 4620, 5850), PbOff = new(3570, 3570, 4050, 5250), PbPeak = new(4500, 4500, 5100, 6200);
    private static readonly SeasonRates BbNormal = new(2425, 2425, 2760, 3860), BbOff = new(2085, 2085, 2425, 3145), BbPeak = new(2870, 2870, 3465, 4620);
    private static readonly SeasonRates IaNormal = new(2100, 2100, 2350, 3465), IaOff = new(1860, 1860, 2085, 2785), IaPeak = new(2600, 2600, 3150, 4140);

    private const string Uploads = "https://bergerhouseboats.com.au/wp-content/uploads/";

    private static readonly SeedBoat[] Boats =
    [
        new("Pacific Blue", "pacific-blue", 12, 6, 2, "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area",
            2000, null, Uploads + "2025/03/1-12.jpg", true, false, PbNormal, PbOff, PbPeak, Active: true),
        new("Ocean Spirit", "ocean-spirit", 12, 6, 0, "4 bedrooms (2 queen, 2 double), queen trundle bed, double sofa bed",
            2000, 1000, Uploads + "2022/11/ocean-spirit-walkthrough-7-600x450.jpg", true, false, PbNormal, PbOff, PbPeak),
        new("Island Dream", "island-dream", 12, 8, 0, "5 bedrooms: upstairs 2 doubles and a double + single; downstairs a king and a double + single; double sofa bed in the lounge",
            2000, 1000, Uploads + "2022/04/island-dream.jpg", false, true,
            new(3360, 3360, 4060, 5095), new(3145, 3145, 3465, 4900), new(3940, 3940, 4570, 5735)),
        new("Kalinda", "kalinda", 12, 5, 2, "4 bedrooms: 3 double, 1 bunk room (2 singles); double sofa bed and double trundle in the lounge",
            2000, null, Uploads + "2025/03/1-10.jpg", false, false,
            new(2535, 2535, 2870, 4080), new(2310, 2310, 2645, 3360), new(3035, 3035, 3570, 4850)),
        new("Blue Bayou", "blue-bayou", 12, 5, 2, "4 bedrooms: 3 double, 1 bunk room (2 singles); double sofa bed and double trundle in the lounge; king-size day-bed on the top deck",
            2000, null, Uploads + "2025/03/3-5.jpg", false, false, BbNormal, BbOff, BbPeak),
        new("Gypsea Belle", "gypsea-belle", 12, 5, 2, "Queen in the main bedroom, bunk room (2 singles), 2 double sofa beds in the lower lounge; upstairs queen with a double trundle",
            2000, null, Uploads + "2025/03/3-4.jpg", false, false, BbNormal, BbOff, BbPeak),
        new("Image Anne", "image-anne", 12, 4, 4, "3 bedrooms: 2 double, bunk room (4 singles); double trundle and double sofa bed in the lounge",
            2000, null, Uploads + "2025/03/2-8.jpg", false, false, IaNormal, IaOff, IaPeak),
        new("Paradise II", "paradise-ii", 10, 4, 2, "3 bedrooms: 2 double, bunk room (2 singles); double trundle and double sofa bed in the lounge",
            2000, null, Uploads + "2025/03/1-6.jpg", false, false, IaNormal, IaOff, IaPeak),
        new("JFS Rhyanna", "jfs-rhyanna", 8, 4, 0, "3 bedrooms, each with a double bed; double sofa bed in the lounge",
            2000, null, Uploads + "2025/03/1-5.jpg", false, false, IaNormal, IaOff, IaPeak),
        new("Marie Claire", "marie-claire", 7, 4, 1, "3 bedrooms: 2 with a double bed, 1 with a double and a single bunk above; lounge converts to a double",
            2000, null, Uploads + "2023/10/mc.river_-600x450.jpg", false, true,
            new(1890, 1890, 2200, 3200), new(1575, 1575, 1890, 2645), new(2260, 2260, 2885, 3940)),
        new("Misty Blue", "misty-blue", 9, 3, 3, "Double bedroom, bunk room (2 singles), double with a single bunk above in the hallway alcove, dining nook converts to a double",
            1500, null, Uploads + "2025/03/2-5.jpg", false, false,
            new(1500, 1500, 1675, 2485), new(1300, 1300, 1500, 1875), new(1760, 1760, 2140, 2895)),
        new("Wavebreak", "wavebreak", 4, 2, 2, "Double bedroom, 2 single bunks near the console, double sofa bed in the lounge",
            1500, null, Uploads + "2023/03/wavebreak-couple-1-600x450.jpg", false, true,
            new(1350, 1350, 1565, 2100), new(1190, 1190, 1365, 1740), new(1655, 1655, 1990, 2645)),
        new("Wanderer", "wanderer", 6, 2, 2, "Permanent double bed, double vario bed (converts from the couch), 2 single bunks",
            1500, null, Uploads + "2025/03/3-2.jpg", false, true,
            new(1210, 1210, 1435, 1765), new(1080, 1080, 1235, 1545), new(1545, 1545, 1890, 2535)),
        new("Runaway Bear", "runaway-bear", 2, 1, 0, "Open plan with 1 permanent double bed (curtained for privacy)",
            1500, 250, Uploads + "2025/03/3-1.jpg", false, false,
            new(880, 880, 945, 1385), new(810, 810, 880, 1230), new(1035, 1035, 1325, 1630)),
    ];

    private sealed record SeedAddon(string Id, string Name, bool QuantityApplies = false, AddonPrices? Prices = null, int? Capacity = null, string? Unit = null);

    private static readonly SeedAddon[] Addons =
    [
        new("yabby-pumps", "Yabby pumps"),
        new("ice", "Ice", QuantityApplies: true),
        new("bait", "Bait", QuantityApplies: true),
        new("pizza-oven", "Pizza oven"),
        new("fishing-gear", "Fishing gear"),
        new("car-parking", "Car parking", QuantityApplies: true, Capacity: 11, Unit: "car spots"),
        new("grocery-service", "Grocery service"),
        new("on-mooring-jetty-stays", "On mooring jetty stays"),
        new("outdoor-heater", "Outdoor heater"),
        new("outboard-motor", "Outboard motor", Prices: new AddonPrices(95, 95, 130)),
        new("welcome-hamper", "Welcome hamper"),
        new("skipper", "Skipper"),
    ];

    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Seasons
        var existingSeasons = (await seasons.ListAsync(ct)).ToList();
        if (existingSeasons.All(s => !s.IsDefault))
        {
            var normal = Season.CreateNormal(now);
            await seasons.SaveAsync(normal, ct);
            existingSeasons.Add(normal);
        }
        foreach (var (id, name, start, end) in new[] { (OffPeakId, "Off Peak", "01-05", "31-08"), (PeakId, "Peak", "01-12", "31-01") })
        {
            if (existingSeasons.Any(s => s.Id == id || s.Name == name)) continue;
            var season = Season.Rehydrate(id, name, [new SeasonRange(DayMonth.Parse(start, "ranges"), DayMonth.Parse(end, "ranges"))], 0, now, now);
            season.Update(name, season.Ranges, existingSeasons);
            await seasons.SaveAsync(season, ct);
            existingSeasons.Add(season);
        }

        // Blocked periods
        if (await blockedPeriods.GetAsync(ChristmasId, ct) is null && (await blockedPeriods.ListAsync(ct)).Count == 0)
            await blockedPeriods.SaveAsync(BlockedPeriod.Rehydrate(ChristmasId, "Christmas / New Year 2026–27",
                new NightRange(new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5)), true, 0, now, now), ct);

        // Add-ons, in the brief's order
        var existingAddons = await addons.ListAsync(ct);
        var order = existingAddons.Count == 0 ? 0 : existingAddons.Max(a => a.SortOrder) + 1;
        foreach (var a in Addons)
        {
            if (existingAddons.Any(x => x.Id == a.Id)) continue;
            var addon = AddonDefinition.Rehydrate(a.Id, a.Name, null, a.Prices is null, a.Prices, a.QuantityApplies, a.Capacity, a.Unit,
                order++, true, 0, now, now);
            await addons.SaveAsync(addon, ct);
        }
        var addonIds = (await addons.ListAsync(ct)).Select(a => a.Id).ToList();

        // Boats: only Pacific Blue is active; staff verify the rest in Fleet.
        foreach (var s in Boats)
        {
            if (await boats.GetBySlugAsync(s.Slug, ct) is not null) continue;
            var boat = Boat.Create(new BoatDetails(s.Name, s.Slug, BoatType.House, s.Berth, s.AdultBeds, s.KidsBeds, s.Bedding, s.Bond,
                s.Deposit, s.ImageUrl, s.Spa, s.Pets), now);
            boat.SetRates(Rates(Season.NormalId, s.Normal).Concat(Rates(OffPeakId, s.OffPeak)).Concat(Rates(PeakId, s.Peak)));
            boat.SetAllowedAddons(addonIds);
            if (s.Active) boat.Activate(existingSeasons);
            await boats.SaveAsync(boat, ct);
        }

        // Email templates
        var existingTemplates = await templates.ListAsync(ct);
        foreach (var (key, (subject, body)) in DefaultEmailTemplates.All)
            if (existingTemplates.All(t => t.Key != key))
                await templates.SaveAsync(EmailTemplate.Create(key, subject, body, now), ct);

        // Business settings
        if (await settings.GetAsync(ct) is null)
        {
            var defaults = ReferenceDataLoader.DefaultSettings(SydneyTime.Today(now));
            await settings.SaveAsync(BusinessSettings.Create(defaults with
            {
                BookingsOpenUntil = new DateOnly(2027, 11, 30),
                StaffAlertEmail = options.StaffAlertEmail,
                ContactPhone = options.ContactPhone ?? "(phone to be added)",
                ContactEmail = options.ContactEmail,
            }, now), ct);
        }
        logger.LogInformation("Seed complete");
    }

    private static IEnumerable<BoatRate> Rates(string seasonId, SeasonRates r) =>
    [
        new(seasonId, PeriodType.Weekend, r.Weekend),
        new(seasonId, PeriodType.Midweek, r.Midweek),
        new(seasonId, PeriodType.LongWeekend, r.LongWeekend),
        new(seasonId, PeriodType.Week, r.Week),
    ];
}
