using BergerHarbour.Application.Abstractions;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore;

[Collection(EmulatorCollection.Name)]
public class FirestoreBookingReferenceGeneratorTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Two_bookings_with_the_same_prefix_get_distinct_suffixes_under_concurrency()
    {
        var prefix = "CONCURRENT-" + Random.Shared.Next(100000, 999999);
        var generator = fx.Get<IBookingReferenceGenerator>();
        var suffixes = await Task.WhenAll(generator.NextSuffixAsync(prefix), generator.NextSuffixAsync(prefix));
        Assert.Equal([0, 1], suffixes.Order());
        Assert.Equal(2, await generator.NextSuffixAsync(prefix));
    }
}
