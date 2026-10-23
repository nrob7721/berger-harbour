using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Seeding;

/// <summary>
/// Creates the initial data. Idempotent: it uses stable ids and only creates what is missing, so it never
/// overwrites anything staff have edited.
/// </summary>
public sealed class SeedService(
    IBusinessSettingsRepository business,
    ISeasonRepository seasons,
    IBlockedPeriodRepository blockedPeriods,
    IAddonDefinitionRepository addons,
    IEmailTemplateRepository templates,
    IBoatRepository boats,
    IClock clock,
    ILogger<SeedService> logger)
{
    public static readonly SeasonId OffPeakId = new("off-peak");
    public static readonly SeasonId PeakId = new("peak");

    // Weekend / Midweek / LongWeekend / Week. Source: each boat's page on bergerhouseboats.com.au.
    private sealed record SeasonRates(decimal Weekend, decimal Midweek, decimal LongWeekend, decimal Week)
    {
        public static SeasonRates Of(decimal weekendOrMidweek, decimal longWeekend, decimal week) =>
            new(weekendOrMidweek, weekendOrMidweek, longWeekend, week);
    }

    /// <param name="AdultBeds">Doubles, queens, kings, sofa beds, trundles and other non-bunk singles.</param>
    /// <param name="KidsBeds">Bunks.</param>
    /// <param name="Deposit">Null uses the Settings default.</param>
    /// <param name="Image">The first image on the boat's page, relative to <see cref="WordPressUploads"/>.</param>
    private sealed record FleetSeed(string Name, string Slug, int MaxGuests, int AdultBeds, int KidsBeds, string Bedding,
        decimal Bond, decimal? Deposit, SeasonRates Normal, SeasonRates OffPeak, SeasonRates Peak, bool Active = false,
        string? Image = null, bool Spa = false, bool PetFriendly = false);

    private const string WordPressUploads = "https://bergerhouseboats.com.au/wp-content/uploads/";

    private static readonly SeasonRates SpaBoatNormal = SeasonRates.Of(4060m, 4620m, 5850m);
    private static readonly SeasonRates SpaBoatOffPeak = SeasonRates.Of(3570m, 4050m, 5250m);
    private static readonly SeasonRates SpaBoatPeak = SeasonRates.Of(4500m, 5100m, 6200m);

    private static readonly SeasonRates MidBoatNormal = SeasonRates.Of(2425m, 2760m, 3860m);
    private static readonly SeasonRates MidBoatOffPeak = SeasonRates.Of(2085m, 2425m, 3145m);
    private static readonly SeasonRates MidBoatPeak = SeasonRates.Of(2870m, 3465m, 4620m);

    private static readonly SeasonRates SmallBoatNormal = SeasonRates.Of(2100m, 2350m, 3465m);
    private static readonly SeasonRates SmallBoatOffPeak = SeasonRates.Of(1860m, 2085m, 2785m);
    private static readonly SeasonRates SmallBoatPeak = SeasonRates.Of(2600m, 3150m, 4140m);

    /// <summary>Pacific Blue is seeded active as the reference boat; the rest stay inactive until staff verify them.</summary>
    private static readonly FleetSeed[] Fleet =
    [
        new("Pacific Blue", "pacific-blue", 12, 6, 2,
            "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area", 2000m, 1000m,
            SpaBoatNormal, SpaBoatOffPeak, SpaBoatPeak, Active: true,
            Image: "2025/03/1-12.jpg", Spa: true),
        new("Ocean Spirit", "ocean-spirit", 12, 6, 0,
            "2 queen bedrooms, 2 double bedrooms, queen trundle bed, double sofa bed", 2000m, 1000m,
            SpaBoatNormal, SpaBoatOffPeak, SpaBoatPeak, Active: true,
            Image: "2022/11/ocean-spirit-walkthrough-7-600x450.jpg", Spa: true),
        new("Island Dream", "island-dream", 12, 8, 0,
            "Upper deck: 2 double bedrooms and a double + single bedroom. Lower deck: king bedroom and a double + single bedroom. Double sofa bed in lounge",
            2000m, 1000m,
            SeasonRates.Of(3360m, 4060m, 5095m), SeasonRates.Of(3145m, 3465m, 4900m), SeasonRates.Of(3940m, 4570m, 5735m), Active: true,
            Image: "2022/04/island-dream.jpg", PetFriendly: true),
        new("Kalinda", "kalinda", 12, 5, 2,
            "3 double bedrooms, bunk room (2 singles), double sofa bed and double trundle in lounge", 2000m, null,
            SeasonRates.Of(2535m, 2870m, 4080m), SeasonRates.Of(2310m, 2645m, 3360m), SeasonRates.Of(3035m, 3570m, 4850m), Active: true,
            Image: "2025/03/1-10.jpg"),
        new("Blue Bayou", "blue-bayou", 12, 5, 2,
            "3 double bedrooms, bunk room (2 singles), double sofa bed and double trundle in lounge", 2000m, null,
            MidBoatNormal, MidBoatOffPeak, MidBoatPeak, Active: true,
            Image: "2025/03/3-5.jpg"),
        new("Gypsea Belle", "gypsea-belle", 12, 5, 2,
            "2 queen bedrooms (one with a double trundle), bunk room (2 singles), 2 double sofa beds in lower lounge", 2000m, null,
            MidBoatNormal, MidBoatOffPeak, MidBoatPeak, Active: true,
            Image: "2025/03/3-4.jpg"),
        new("Image Anne", "image-anne", 12, 4, 4,
            "2 double bedrooms, bunk room (4 singles), double trundle and double sofa bed in lounge", 2000m, null,
            SmallBoatNormal, SmallBoatOffPeak, SmallBoatPeak, Active: true,
            Image: "2025/03/2-8.jpg"),
        new("Paradise II", "paradise-ii", 10, 4, 2,
            "2 double bedrooms, bunk room (2 singles), double trundle and double sofa bed in lounge", 2000m, null,
            SmallBoatNormal, SmallBoatOffPeak, SmallBoatPeak, Active: true,
            Image: "2025/03/1-6.jpg"),
        new("JFS Rhyanna", "jfs-rhyanna", 8, 4, 0,
            "3 double bedrooms, double sofa bed", 2000m, null,
            SmallBoatNormal, SmallBoatOffPeak, SmallBoatPeak, Active: true,
            Image: "2025/03/1-5.jpg"),
        new("Marie Claire", "marie-claire", 7, 4, 1,
            "3 double bedrooms (one with a single bunk above), double sofa bed in lounge", 2000m, null,
            SeasonRates.Of(1890m, 2200m, 3200m), SeasonRates.Of(1575m, 1890m, 2645m), SeasonRates.Of(2260m, 2885m, 3940m), Active: true,
            Image: "2023/10/mc.river_-600x450.jpg", PetFriendly: true),
        new("Misty Blue", "misty-blue", 9, 3, 3,
            "Double bedroom, bunk room (2 singles), double bed with single bunk above in hallway alcove, dining nook converts to a double",
            1500m, null,
            SeasonRates.Of(1500m, 1675m, 2485m), SeasonRates.Of(1300m, 1500m, 1875m), SeasonRates.Of(1760m, 2140m, 2895m), Active: true,
            Image: "2025/03/2-5.jpg"),
        new("Wavebreak", "wavebreak", 4, 2, 2,
            "Open plan: double bed, 2 single bunks, double sofa bed in lounge", 1500m, null,
            SeasonRates.Of(1350m, 1565m, 2100m), SeasonRates.Of(1190m, 1365m, 1740m), SeasonRates.Of(1655m, 1990m, 2645m), Active: true,
            Image: "2023/03/wavebreak-couple-1-600x450.jpg", PetFriendly: true),
        new("Wanderer", "wanderer", 6, 2, 2,
            "Open plan: permanent double bed, double vario couch bed, 2 single bunks", 1500m, null,
            SeasonRates.Of(1210m, 1435m, 1765m), SeasonRates.Of(1080m, 1235m, 1545m), SeasonRates.Of(1545m, 1890m, 2535m), Active: true,
            Image: "2025/03/3-2.jpg", PetFriendly: true),
        new("Runaway Bear", "runaway-bear", 2, 1, 0,
            "Open plan: double bed", 1500m, 250m,
            SeasonRates.Of(880m, 945m, 1385m), SeasonRates.Of(810m, 880m, 1230m), SeasonRates.Of(1035m, 1325m, 1630m), Active: true,
            Image: "2025/03/3-1.jpg"),
    ];

    /// <summary>Seeded in this order. Car parking is shared by every booking: 11 spots in total.</summary>
    private static readonly (string Id, string Name, bool QuantityApplies, AddonPrices? Prices, int? Capacity, string? Unit)[] Addons =
    [
        ("yabby-pumps", "Yabby pumps", false, null, null, null),
        ("ice", "Ice", true, null, null, null),
        ("bait", "Bait", true, null, null, null),
        ("pizza-oven", "Pizza oven", false, null, null, null),
        ("fishing-gear", "Fishing gear", false, null, null, null),
        ("car-parking", "Car parking", true, null, 11, "car spots"),
        ("grocery-service", "Grocery service", false, null, null, null),
        ("on-mooring-jetty-stays", "On mooring jetty stays", false, null, null, null),
        ("outdoor-heater", "Outdoor heater", false, null, null, null),
        ("outboard-motor", "Outboard motor", false, new AddonPrices(95m, 95m, 130m), null, null),
        ("welcome-hamper", "Welcome hamper", false, null, null, null),
        ("skipper", "Skipper", false, null, null, null),
    ];

    public sealed record SeedOptions(string StaffAlertEmail, string ContactPhone, string ContactEmail);

    public async Task<int> RunAsync(SeedOptions options, CancellationToken ct = default)
    {
        var created = 0;
        var now = clock.UtcNow;

        if (await business.FindAsync(ct) is null)
        {
            await business.SaveAsync(BusinessSettings.CreateDefault(new DateOnly(2027, 11, 30), options.StaffAlertEmail,
                options.ContactPhone, options.ContactEmail), ct);
            created++;
        }

        var existingSeasons = (await seasons.ListAsync(ct)).Select(s => s.Id).ToHashSet();
        foreach (var season in new[]
                 {
                     Season.CreateNormal(),
                     Season.CreateWithId(OffPeakId, "Off Peak", [new DayMonthRange(new DayMonth(1, 5), new DayMonth(31, 8))]),
                     Season.CreateWithId(PeakId, "Peak", [new DayMonthRange(new DayMonth(1, 12), new DayMonth(31, 1))]),
                 })
        {
            if (!existingSeasons.Contains(season.Id))
            {
                await seasons.SaveAsync(season, ct);
                created++;
            }
        }

        var christmasId = new BlockedPeriodId("christmas-new-year-2026-27");
        if (await blockedPeriods.GetAsync(christmasId, ct) is null)
        {
            await blockedPeriods.SaveAsync(BlockedPeriod.CreateWithId(christmasId, "Christmas / New Year 2026–27",
                new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5), true), ct);
            created++;
        }

        var existingAddons = (await addons.ListAsync(ct)).Select(a => a.Id).ToHashSet();
        for (var position = 0; position < Addons.Length; position++)
        {
            var (id, name, quantityApplies, prices, capacity, unit) = Addons[position];
            if (!existingAddons.Contains(new AddonId(id)))
            {
                await addons.SaveAsync(AddonDefinition.CreateWithId(new AddonId(id), name, null, prices is null, prices,
                    quantityApplies, capacity, unit, position), ct);
                created++;
            }
        }

        var existingTemplates = (await templates.ListAsync(ct)).Select(t => t.Key).ToHashSet();
        foreach (var template in DefaultEmailTemplates.All().Where(t => !existingTemplates.Contains(t.Key)))
        {
            await templates.SaveAsync(template, ct);
            created++;
        }

        var allSeasons = await seasons.ListAsync(ct);
        foreach (var seed in Fleet)
        {
            if (await boats.GetAsync(new BoatId(seed.Slug), ct) is not null || await boats.GetBySlugAsync(seed.Slug, ct) is not null)
            {
                continue;
            }

            var boat = Boat.CreateWithId(new BoatId(seed.Slug), seed.Name, seed.Slug, BoatType.House, seed.MaxGuests, now);
            Complete(boat, seed, allSeasons, now);
            await boats.SaveAsync(boat, ct);
            created++;
        }

        logger.LogInformation("Seed complete: {Created} records created", created);
        return created;
    }

    private static void Complete(Boat boat, FleetSeed seed, IReadOnlyList<Season> allSeasons, DateTimeOffset now)
    {
        boat.UpdateDetails(seed.Name, seed.Slug, seed.MaxGuests, seed.AdultBeds, seed.KidsBeds, seed.Bedding, seed.Bond,
            seed.Deposit, now);
        boat.UpdateFeatures(seed.Image is null ? null : WordPressUploads + seed.Image, seed.Spa, seed.PetFriendly, now);
        var table = new Dictionary<SeasonId, SeasonRates>
        {
            [SeasonId.Normal] = seed.Normal,
            [OffPeakId] = seed.OffPeak,
            [PeakId] = seed.Peak,
        };
        boat.SetRates(table.SelectMany(t => new[]
        {
            new BoatRate(t.Key, PeriodType.Weekend, t.Value.Weekend),
            new BoatRate(t.Key, PeriodType.Midweek, t.Value.Midweek),
            new BoatRate(t.Key, PeriodType.LongWeekend, t.Value.LongWeekend),
            new BoatRate(t.Key, PeriodType.Week, t.Value.Week),
        }), now);
        boat.SetAllowedAddons(Addons.Select(a => new AddonId(a.Id)), now);
        if (seed.Active)
        {
            boat.Activate(allSeasons, now);
        }
    }
}
