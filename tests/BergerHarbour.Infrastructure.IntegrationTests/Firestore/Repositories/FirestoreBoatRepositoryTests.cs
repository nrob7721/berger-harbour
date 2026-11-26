using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBoatRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Seeded_boats_round_trip_with_rates_allowed_addons_photos_and_tags()
    {
        var boats = await fx.Get<IBoatRepository>().ListAsync();
        Assert.Equal(14, boats.Count);
        var pacific = (await fx.Get<IBoatRepository>().GetBySlugAsync("pacific-blue"))!;
        Assert.True(pacific.IsActive);
        Assert.Equal((12, 6, 2), (pacific.Berth, pacific.AdultBeds!.Value, pacific.KidsBeds!.Value));
        Assert.Equal(12, pacific.Rates.Count);
        Assert.Equal(4060m, pacific.RateFor(new Domain.Settings.SeasonId("normal"), PeriodType.Midweek));
        Assert.Equal(12, pacific.AllowedAddonIds.Count);
        Assert.Null(pacific.DepositAmount);
        Assert.Equal(250m, (await fx.Get<IBoatRepository>().GetBySlugAsync("runaway-bear"))!.DepositAmount);
        Assert.All(boats, b => Assert.True(b.Berth >= b.TotalBeds));
        Assert.Equal("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", pacific.ThumbnailUrl);
        Assert.Null(pacific.Images[0].Description);
        Assert.Equal("Board from the rear deck straight into the large lounge and dining area.", pacific.Images[3].Description);
        Assert.Equal("Sleeps 12", pacific.Tags[0]);
        Assert.All(boats, b => Assert.NotEmpty(b.Images));
        Assert.All(boats, b => Assert.NotEmpty(b.Tags));
    }

    [Fact]
    public async Task Unknown_ids_are_not_found() => Assert.Null(await fx.Get<IBoatRepository>().GetAsync(new BoatId("no/such")));
}
