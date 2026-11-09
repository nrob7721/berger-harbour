using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Seed;

/// <summary>
/// The setup seed (§13). Idempotent: it only creates what is missing and never overwrites staff edits. Boat data comes
/// from each boat's page on bergerhouseboats.com.au; only Pacific Blue is active until staff verify the others in Fleet.
/// </summary>
public sealed class SeedService
{
    private static readonly SeasonId Normal = new("normal");
    private static readonly SeasonId OffPeak = new("off-peak");
    private static readonly SeasonId Peak = new("peak");

    private static readonly SeedBoat[] Boats =
    [
        new("Pacific Blue", "pacific-blue", 12, 6, 2, "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", true, false,
            [4060, 4060, 4620, 5850], [3570, 3570, 4050, 5250], [4500, 4500, 5100, 6200], true),
        new("Ocean Spirit", "ocean-spirit", 12, 6, 0, "4 bedrooms (2 queen, 2 double), queen trundle bed, double sofa bed", 2000, 1000,
            "https://bergerhouseboats.com.au/wp-content/uploads/2022/11/ocean-spirit-walkthrough-7-600x450.jpg", true, false,
            [4060, 4060, 4620, 5850], [3570, 3570, 4050, 5250], [4500, 4500, 5100, 6200]),
        new("Island Dream", "island-dream", 12, 8, 0,
            "5 bedrooms: upper deck 2 doubles and 1 double + 1 single; lower deck 1 king and 1 double + 1 single; double sofa bed in lounge", 2000, 1000,
            "https://bergerhouseboats.com.au/wp-content/uploads/2022/04/island-dream.jpg", false, true,
            [3360, 3360, 4060, 5095], [3145, 3145, 3465, 4900], [3940, 3940, 4570, 5735]),
        new("Kalinda", "kalinda", 12, 5, 2, "3 double bedrooms, bunk room (2 singles), double sofa lounge, double trundle", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-10.jpg", false, false,
            [2535, 2535, 2870, 4080], [2310, 2310, 2645, 3360], [3035, 3035, 3570, 4850]),
        new("Blue Bayou", "blue-bayou", 12, 6, 2,
            "3 double bedrooms, bunk room (2 singles), double sofa bed and double trundle in lounge, king day-bed on top deck", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/3-5.jpg", false, false,
            [2425, 2425, 2760, 3860], [2085, 2085, 2425, 3145], [2870, 2870, 3465, 4620]),
        new("Gypsea Belle", "gypsea-belle", 12, 5, 2, "2 queen bedrooms, bunk room (2 singles), double trundle, 2 double sofa beds", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/3-4.jpg", false, false,
            [2425, 2425, 2760, 3860], [2085, 2085, 2425, 3145], [2870, 2870, 3465, 4620]),
        new("Image Anne", "image-anne", 12, 4, 4, "2 double bedrooms, bunk room (4 singles), double trundle and double sofa bed in lounge", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/2-8.jpg", false, false,
            [2100, 2100, 2350, 3465], [1860, 1860, 2085, 2785], [2600, 2600, 3150, 4140]),
        new("Paradise II", "paradise-ii", 10, 4, 2, "2 double bedrooms, bunk room (2 singles), double trundle and double sofa bed in lounge", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-6.jpg", false, false,
            [2100, 2100, 2350, 3465], [1860, 1860, 2085, 2785], [2600, 2600, 3150, 4140]),
        new("JFS Rhyanna", "jfs-rhyanna", 8, 4, 0, "3 double bedrooms, double sofa bed", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-5.jpg", false, false,
            [2100, 2100, 2350, 3465], [1860, 1860, 2085, 2785], [2600, 2600, 3150, 4140]),
        new("Marie Claire", "marie-claire", 7, 4, 1, "2 double bedrooms, bedroom with double/single bunk, lounge converts to a double", 2000, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2023/10/mc.river_-600x450.jpg", false, true,
            [1890, 1890, 2200, 3200], [1575, 1575, 1890, 2645], [2260, 2260, 2885, 3940]),
        new("Misty Blue", "misty-blue", 9, 3, 3,
            "Double bedroom, bunk room (2 singles), double with single bunk above in an alcove, dining nook converts to a double", 1500, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/2-5.jpg", false, false,
            [1500, 1500, 1675, 2485], [1300, 1300, 1500, 1875], [1760, 1760, 2140, 2895]),
        new("Wavebreak", "wavebreak", 4, 2, 2, "Double bedroom with 2 single bunks, double sofa bed in lounge", 1500, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2023/03/wavebreak-couple-1-600x450.jpg", false, true,
            [1350, 1350, 1565, 2100], [1190, 1190, 1365, 1740], [1655, 1655, 1990, 2645]),
        new("Wanderer", "wanderer", 6, 2, 2, "Permanent double bed, double vario bed, 2 single bunks", 1500, null,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/3-2.jpg", false, true,
            [1210, 1210, 1435, 1765], [1080, 1080, 1235, 1545], [1545, 1545, 1890, 2535]),
        new("Runaway Bear", "runaway-bear", 2, 1, 0, "Double bed", 1500, 250,
            "https://bergerhouseboats.com.au/wp-content/uploads/2025/03/3-1.jpg", false, false,
            [880, 880, 945, 1385], [810, 810, 880, 1230], [1035, 1035, 1325, 1630]),
    ];

    private static readonly SeedAddon[] Addons =
    [
        new("yabby-pumps", "Yabby pumps", true, null, false, null, null),
        new("ice", "Ice", true, null, true, null, null),
        new("bait", "Bait", true, null, true, null, null),
        new("pizza-oven", "Pizza oven", true, null, false, null, null),
        new("fishing-gear", "Fishing gear", true, null, false, null, null),
        new("car-parking", "Car parking", true, null, true, 11, "car spots"),
        new("grocery-service", "Grocery service", true, null, false, null, null),
        new("on-mooring-jetty-stays", "On mooring jetty stays", true, null, false, null, null),
        new("outdoor-heater", "Outdoor heater", true, null, false, null, null),
        new("outboard-motor", "Outboard motor", false, new AddonPrices(95, 95, 130), false, null, null),
        new("welcome-hamper", "Welcome hamper", true, null, false, null, null),
        new("skipper", "Skipper", true, null, false, null, null),
    ];

    private readonly IBusinessSettingsRepository settings;
    private readonly ISeasonRepository seasons;
    private readonly IBlockedPeriodRepository blockedPeriods;
    private readonly IAddonDefinitionRepository addons;
    private readonly IEmailTemplateRepository templates;
    private readonly IBoatRepository boats;
    private readonly IClock clock;
    private readonly ILogger<SeedService> logger;

    public SeedService(
        IBusinessSettingsRepository settings, ISeasonRepository seasons, IBlockedPeriodRepository blockedPeriods, IAddonDefinitionRepository addons,
        IEmailTemplateRepository templates, IBoatRepository boats, IClock clock, ILogger<SeedService> logger)
    {
        this.settings = settings;
        this.seasons = seasons;
        this.blockedPeriods = blockedPeriods;
        this.addons = addons;
        this.templates = templates;
        this.boats = boats;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        if (await settings.GetAsync(ct) is null)
        {
            await settings.SaveAsync(BusinessSettings.Create(BusinessSettings.DefaultDeposit, BusinessSettings.DefaultLeadTimeDays, new DateOnly(2027, 11, 30),
                options.StaffAlertEmail, options.ContactPhone, options.ContactEmail, BusinessSettings.DefaultHireTermsUrl, null, null, null), ct);
        }

        var existingSeasons = (await seasons.ListAsync(ct)).ToList();
        if (existingSeasons.Count == 0)
        {
            var normal = Season.CreateDefault(Normal);
            var offPeak = Season.Create(OffPeak, "Off Peak", [new SeasonRange(DayMonth.Create(1, 5), DayMonth.Create(31, 8))], [normal]);
            var peak = Season.Create(Peak, "Peak", [new SeasonRange(DayMonth.Create(1, 12), DayMonth.Create(31, 1))], [normal, offPeak]);
            foreach (var season in new[] { normal, offPeak, peak })
            {
                await seasons.SaveAsync(season, ct);
                existingSeasons.Add(season);
            }
        }

        if ((await blockedPeriods.ListAsync(ct)).Count == 0)
        {
            await blockedPeriods.SaveAsync(BlockedPeriod.Create(new BlockedPeriodId("christmas-new-year-2026-27"), "Christmas / New Year 2026–27",
                NightRange.Create(new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5)), true), ct);
        }

        var existingAddons = await addons.ListAsync(ct);
        if (existingAddons.Count == 0)
        {
            var order = 0;
            foreach (var a in Addons)
            {
                await addons.SaveAsync(AddonDefinition.Create(new AddonId(a.Id), a.Name, null, a.PriceOnRequest, a.Prices ?? AddonPrices.None, a.QuantityApplies,
                    a.Capacity, a.CapacityUnitLabel, order++), ct);
            }

            existingAddons = await addons.ListAsync(ct);
        }

        var existingTemplates = (await templates.ListAsync(ct)).Select(t => t.Key).ToHashSet();
        foreach (var (key, subject, body) in DefaultEmailTemplates.All.Where(t => !existingTemplates.Contains(t.Key)))
        {
            await templates.SaveAsync(EmailTemplate.Create(key, subject, body, EmailPlaceholders.Known), ct);
        }

        var existingBoats = (await boats.ListAsync(ct)).Select(b => b.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allAddonIds = existingAddons.Select(a => a.Id).ToList();
        foreach (var seed in Boats.Where(b => !existingBoats.Contains(b.Slug)))
        {
            var boat = Boat.Create(new BoatId(seed.Slug), seed.ToDetails(allAddonIds), clock.UtcNow);
            if (seed.Active)
            {
                boat.Activate(existingSeasons, clock.UtcNow);
            }

            await boats.SaveAsync(boat, ct);
        }

        logger.LogInformation("Seed complete.");
    }

    /// <summary>Rates in the order the website lists them: Weekend, Midweek, Long weekend, Week.</summary>
    private sealed record SeedBoat(
        string Name, string Slug, int Berth, int AdultBeds, int KidsBeds, string Bedding, decimal Bond, decimal? Deposit, string ImageUrl, bool Spa,
        bool Pets, decimal[] NormalRates, decimal[] OffPeakRates, decimal[] PeakRates, bool Active = false)
    {
        public BoatDetails ToDetails(IReadOnlyList<AddonId> addonIds) =>
            new(Name, Slug, BoatType.House, Berth, AdultBeds, KidsBeds, Bedding, Bond, Deposit, ImageUrl, Spa, Pets,
                [.. Rates(SeedService.Normal, NormalRates), .. Rates(SeedService.OffPeak, OffPeakRates), .. Rates(SeedService.Peak, PeakRates)], addonIds);

        private static IEnumerable<BoatRate> Rates(SeasonId season, decimal[] r) =>
        [
            new(season, PeriodType.Weekend, r[0]),
            new(season, PeriodType.Midweek, r[1]),
            new(season, PeriodType.LongWeekend, r[2]),
            new(season, PeriodType.Week, r[3]),
        ];
    }

    private sealed record SeedAddon(
        string Id, string Name, bool PriceOnRequest, AddonPrices? Prices, bool QuantityApplies, int? Capacity, string? CapacityUnitLabel);
}
