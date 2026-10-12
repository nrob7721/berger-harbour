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

    private static readonly (string Name, string Slug, int MaxGuests)[] Fleet =
    [
        ("Pacific Blue", "pacific-blue", 12),
        ("Ocean Spirit", "ocean-spirit", 12),
        ("Island Dream", "island-dream", 12),
        ("Kalinda", "kalinda", 12),
        ("Blue Bayou", "blue-bayou", 12),
        ("Gypsea Belle", "gypsea-belle", 12),
        ("Image Anne", "image-anne", 12),
        ("Paradise II", "paradise-ii", 10),
        ("JFS Rhyanna", "jfs-rhyanna", 8),
        ("Marie Claire", "marie-claire", 7),
        ("Misty Blue", "misty-blue", 9),
        ("Wavebreak", "wavebreak", 4),
        ("Wanderer", "wanderer", 6),
        ("Runaway Bear", "runaway-bear", 2),
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
        foreach (var (name, slug, maxGuests) in Fleet)
        {
            if (await boats.GetAsync(new BoatId(slug), ct) is not null || await boats.GetBySlugAsync(slug, ct) is not null)
            {
                continue;
            }

            var boat = Boat.CreateWithId(new BoatId(slug), name, slug, BoatType.House, maxGuests, now);
            if (slug == "pacific-blue")
            {
                CompletePacificBlue(boat, allSeasons, now);
            }

            await boats.SaveAsync(boat, ct);
            created++;
        }

        logger.LogInformation("Seed complete: {Created} records created", created);
        return created;
    }

    /// <summary>Pacific Blue is seeded complete and active as the reference boat.</summary>
    private static void CompletePacificBlue(Boat boat, IReadOnlyList<Season> allSeasons, DateTimeOffset now)
    {
        // 6 adult beds (4 queens + 2 singles) and 2 kids beds (bunks); the deposit is left to the Settings default.
        boat.UpdateDetails(boat.Name, boat.Slug, 12, 6, 2,
            "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area", 2000m, null, now);
        // (Weekend, Midweek, LongWeekend, Week)
        var table = new Dictionary<SeasonId, (decimal Weekend, decimal Midweek, decimal LongWeekend, decimal Week)>
        {
            [SeasonId.Normal] = (4060m, 4060m, 4620m, 5850m),
            [OffPeakId] = (3570m, 3570m, 4050m, 5250m),
            [PeakId] = (4500m, 4500m, 5100m, 6200m),
        };
        boat.SetRates(table.SelectMany(t => new[]
        {
            new BoatRate(t.Key, PeriodType.Weekend, t.Value.Weekend),
            new BoatRate(t.Key, PeriodType.Midweek, t.Value.Midweek),
            new BoatRate(t.Key, PeriodType.LongWeekend, t.Value.LongWeekend),
            new BoatRate(t.Key, PeriodType.Week, t.Value.Week),
        }), now);
        boat.SetAllowedAddons(Addons.Select(a => new AddonId(a.Id)), now);
        boat.Activate(allSeasons, now);
    }
}
