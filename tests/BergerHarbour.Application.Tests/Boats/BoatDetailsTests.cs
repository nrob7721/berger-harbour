using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Boats.Dtos;
using BergerHarbour.Application.Seed;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Boats;

/// <summary>Boat photos, tags and More details in the public and admin APIs (§7.3, §8.1, decision 43), and the seed's backfill (§13).</summary>
public class BoatDetailsTests
{
    private const string Uploads = "https://bergerhouseboats.com.au/wp-content/uploads/";

    private readonly ApplicationHarness h = new();

    private static SaveBoatRequest Request(AdminBoat b, IReadOnlyList<BoatImageDto> images, IReadOnlyList<string> tags) =>
        new(b.Name, b.Slug, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription, b.SecurityBond, b.DepositAmount, images, tags, b.HasSpa,
            b.IsPetFriendly, b.IsActive, b.Rates, b.AllowedAddonIds, b.Version);

    [Fact]
    public async Task Boat_details_list_only_active_house_boats_in_boat_order_with_their_more_details()
    {
        h.SetActiveBoats("pacific-blue", "kalinda", "runaway-bear");
        var response = await h.Get<BoatDetailsService>().ListAsync();

        Assert.Equal(["pacific-blue", "kalinda", "runaway-bear"], response.Boats.Select(b => b.Slug));
        var pacific = response.Boats[0];
        var boat = h.Boat("pacific-blue");
        Assert.Equal(boat.Images.Select(i => (i.Url, i.Description)), pacific.Images.Select(i => (i.Url, i.Description)));
        Assert.Equal($"{Uploads}2025/03/1-12.jpg", pacific.Images[0].Url);
        Assert.Null(pacific.Images[0].Description);
        Assert.Equal("Board from the rear deck straight into the large lounge and dining area.", pacific.Images[3].Description);
        Assert.Equal(boat.BeddingDescription, pacific.BeddingDescription);
        Assert.Equal(2000, pacific.SecurityBond);
        Assert.Equal(h.Db.Settings.All.Single().DepositAmount, pacific.DepositAmount);
        Assert.Equal(12, pacific.Rates.Count);
        Assert.Contains(pacific.Rates, r => r.SeasonId == "normal" && r.PeriodType == PeriodType.LongWeekend && r.Price == 4620);
    }

    [Fact]
    public async Task Boat_details_give_the_effective_deposit()
    {
        h.SetActiveBoats("pacific-blue", "ocean-spirit");
        var boats = (await h.Get<BoatDetailsService>().ListAsync()).Boats;
        Assert.Equal(1000, boats.Single(b => b.Slug == "ocean-spirit").DepositAmount);
        Assert.Equal(h.Db.Settings.All.Single().DepositAmount, boats.Single(b => b.Slug == "pacific-blue").DepositAmount);
    }

    [Fact]
    public async Task Boat_details_list_every_season_with_its_ranges_Normal_first_then_by_earliest_range()
    {
        var seasons = (await h.Get<BoatDetailsService>().ListAsync()).Seasons;
        Assert.Equal(["Normal", "Off Peak", "Peak"], seasons.Select(s => s.Name));
        Assert.Empty(seasons[0].Ranges);
        Assert.Equal(("01-05", "31-08"), (seasons[1].Ranges.Single().Start, seasons[1].Ranges.Single().End));
        Assert.Equal(("01-12", "31-01"), (seasons[2].Ranges.Single().Start, seasons[2].Ranges.Single().End));
    }

    [Fact]
    public async Task Public_boats_give_the_thumbnail_url_and_tags_in_order()
    {
        h.SetActiveBoats("pacific-blue", "kalinda");
        var kalinda = h.Boat("kalinda");
        kalinda.Update(Details(kalinda, [], ["Second", "First"]), h.Db.Seasons.All, h.Clock.UtcNow);
        h.Db.Boats.Save(kalinda);

        var boats = (await h.Get<BoatCatalogueService>().ListAsync()).Boats;
        var pacific = boats.Single(b => b.Slug == "pacific-blue");
        Assert.Equal($"{Uploads}2025/03/1-12.jpg", pacific.ThumbnailUrl);
        Assert.Equal("Sleeps 12", pacific.Tags[0]);
        Assert.Equal("Safety equipment", pacific.Tags[^1]);
        var withoutPhotos = boats.Single(b => b.Slug == "kalinda");
        Assert.Equal(string.Empty, withoutPhotos.ThumbnailUrl);
        Assert.Equal(["Second", "First"], withoutPhotos.Tags);
    }

    [Fact]
    public async Task One_boat_gives_images_tags_and_rates_also_when_inactive()
    {
        h.Deactivate("pacific-blue");
        var boat = await h.Get<BoatCatalogueService>().GetAsync("pacific-blue");
        Assert.False(boat.IsActive);
        Assert.Equal($"{Uploads}2025/03/1-12.jpg", boat.ThumbnailUrl);
        Assert.Equal(8, boat.Images.Count);
        Assert.Equal(19, boat.Tags.Count);
        Assert.Equal(12, boat.Rates.Count);
    }

    [Fact]
    public async Task Saving_a_boat_keeps_the_images_and_tags_in_the_order_sent()
    {
        var service = h.Get<BoatService>();
        var b = await service.GetAsync(h.Boat("kalinda").Id.Value);
        var images = new List<BoatImageDto> { new($"{Uploads}b.jpg", "  Upper deck "), new($"{Uploads}a.jpg", null), new($"{Uploads}c.jpg", " ") };
        var saved = await service.UpdateAsync(b.Id, Request(b, images, ["Zebra", " Apple "]));

        Assert.Equal([$"{Uploads}b.jpg", $"{Uploads}a.jpg", $"{Uploads}c.jpg"], saved.Images.Select(i => i.Url));
        Assert.Equal(["Upper deck", null, null], saved.Images.Select(i => i.Description));
        Assert.Equal($"{Uploads}b.jpg", saved.ThumbnailUrl);
        Assert.Equal(["Zebra", "Apple"], saved.Tags);
        Assert.Equal(["Zebra", "Apple"], h.Boat("kalinda").Tags);
    }

    [Fact]
    public async Task Saving_a_boat_reports_errors_on_the_image_or_tag_concerned()
    {
        var service = h.Get<BoatService>();
        var b = await service.GetAsync(h.Boat("kalinda").Id.Value);
        var images = new List<BoatImageDto> { new($"{Uploads}a.jpg", null), new("https://example.com/b.jpg", new string('x', 121)) };
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(b.Id, Request(b, images, [])));
        Assert.Equal(["Use an https:// link to an image on bergerhouseboats.com.au."], e.Errors.ByField["images[1].url"]);
        Assert.Equal(["Keep the description to 120 characters."], e.Errors.ByField["images[1].description"]);

        var tagError = await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(b.Id, Request(b, [], ["Spa", "SPA"])));
        Assert.Equal(["This tag is already on the boat."], tagError.Errors.ByField["tags[1]"]);
    }

    [Fact]
    public async Task The_seed_fills_in_photos_and_tags_only_on_existing_boats_that_have_none()
    {
        var bare = h.Boat("kalinda");
        bare.Update(Details(bare, [], []), h.Db.Seasons.All, h.Clock.UtcNow);
        h.Db.Boats.Save(bare);
        var edited = h.Boat("wanderer");
        var mine = BoatImage.Create($"{Uploads}mine.jpg", "Mine");
        edited.Update(Details(edited, [mine], ["My tag"]), h.Db.Seasons.All, h.Clock.UtcNow);
        h.Db.Boats.Save(edited);
        var bond = h.Boat("kalinda").SecurityBond;

        await h.Get<SeedService>().SeedAsync(new SeedOptions(ApplicationHarness.StaffEmail, "02 4455 6677", "info@berger.test"));

        var filled = h.Boat("kalinda");
        Assert.Equal($"{Uploads}2025/03/1-10.jpg", filled.ThumbnailUrl);
        Assert.Equal("Sleeps 12", filled.Tags[0]);
        Assert.Equal(bond, filled.SecurityBond);
        Assert.Equal([mine], h.Boat("wanderer").Images);
        Assert.Equal(["My tag"], h.Boat("wanderer").Tags);
        Assert.Equal(14, h.Db.Boats.All.Count);
    }

    private static BoatDetails Details(Boat b, IReadOnlyList<BoatImage> images, IReadOnlyList<string> tags) =>
        new(b.Name, b.Slug, b.Type, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription, b.SecurityBond, b.DepositAmount, images, tags, b.HasSpa,
            b.IsPetFriendly, b.Rates, b.AllowedAddonIds);
}
