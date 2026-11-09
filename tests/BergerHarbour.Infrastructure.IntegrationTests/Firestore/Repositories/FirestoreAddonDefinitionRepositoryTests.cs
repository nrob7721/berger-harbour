using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreAddonDefinitionRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Seeded_addons_round_trip_in_order_with_prices_and_capacity()
    {
        var addons = await fx.Get<IAddonDefinitionRepository>().ListAsync();
        Assert.Equal("Yabby pumps", addons[0].Name);
        var motor = addons.Single(a => a.Name == "Outboard motor");
        Assert.Equal(new AddonPrices(95, 95, 130), motor.Prices);
        var parking = addons.Single(a => a.Name == "Car parking");
        Assert.Equal((11, "car spots"), (parking.Capacity!.Value, parking.CapacityUnitLabel!));
        Assert.All(addons.Where(a => a.Name != "Outboard motor"), a => Assert.True(a.PriceOnRequest));
    }
}
