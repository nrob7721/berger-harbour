using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBlockedPeriodRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task The_seeded_christmas_period_round_trips()
    {
        var christmas = (await fx.Get<IBlockedPeriodRepository>().ListAsync()).Single(p => p.Name.StartsWith("Christmas", StringComparison.Ordinal));
        Assert.Equal(EmulatorFixture.D("2026-12-20"), christmas.Nights.FirstNight);
        Assert.Equal(EmulatorFixture.D("2027-01-05"), christmas.Nights.LastNight);
        Assert.True(christmas.UsesExtendedPaymentSchedule);
    }
}
