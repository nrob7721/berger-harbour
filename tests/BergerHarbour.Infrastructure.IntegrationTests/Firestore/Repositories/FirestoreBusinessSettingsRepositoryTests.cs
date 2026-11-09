using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBusinessSettingsRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Settings_round_trip()
    {
        var settings = (await fx.Get<IBusinessSettingsRepository>().GetAsync())!;
        Assert.Equal(1000m, settings.DepositAmount);
        Assert.Equal(30, settings.MinimumLeadTimeDays);
        Assert.Equal(EmulatorFixture.D("2027-11-30"), settings.BookingsOpenUntil);
        Assert.Equal("staff@example.com", settings.StaffAlertEmail!.Value);
        Assert.Null(settings.Bsb);
    }
}
