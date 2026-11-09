using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreSeasonRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Seasons_round_trip_including_the_year_wrapping_range()
    {
        var peak = (await fx.Get<ISeasonRepository>().GetAsync(new SeasonId("peak")))!;
        Assert.Equal("01-12", peak.Ranges.Single().Start.ToString());
        Assert.True(peak.Ranges.Single().Wraps);
        Assert.True((await fx.Get<ISeasonRepository>().GetAsync(new SeasonId("normal")))!.IsDefault);
    }

    [Fact]
    public async Task Save_and_delete()
    {
        var season = Season.Create(SeasonId.New(), "Spring test", [new SeasonRange(DayMonth.Create(1, 9), DayMonth.Create(15, 9))], []);
        await fx.Get<ISeasonRepository>().SaveAsync(season);
        Assert.Equal(1, season.Version);
        await fx.Get<ISeasonRepository>().DeleteAsync(season);
        Assert.Null(await fx.Get<ISeasonRepository>().GetAsync(season.Id));
    }
}
