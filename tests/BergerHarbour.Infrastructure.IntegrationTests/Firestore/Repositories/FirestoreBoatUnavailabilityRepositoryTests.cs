using BergerHarbour.Application.Unavailabilities;
using BergerHarbour.Application.Unavailabilities.Dtos;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.IntegrationTests.Support;
using static BergerHarbour.Infrastructure.IntegrationTests.Support.EmulatorFixture;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBoatUnavailabilityRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Unavailabilities_are_written_through_the_lock_and_rejected_over_bookings()
    {
        var boat = await fx.BoatAsync("pacific-blue");
        var service = fx.Get<BoatUnavailabilityService>();
        var created = await service.CreateAsync(boat.Id.Value, new SaveUnavailabilityRequest(D("2028-06-05"), D("2028-06-07"), "Engine service", 0));
        Assert.Single(await fx.Get<IBoatUnavailabilityRepository>().ListInRangeAsync(D("2028-06-07"), D("2028-06-08")), u => u.Id.Value == created.Id);
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-06-12", "2028-06-16", email: "unav@example.com"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(boat.Id.Value, created.Id, new SaveUnavailabilityRequest(D("2028-06-05"), D("2028-06-12"), null, created.Version)));
        await service.DeleteAsync(boat.Id.Value, created.Id);
        Assert.Null(await fx.Get<IBoatUnavailabilityRepository>().GetAsync(new BoatUnavailabilityId(created.Id)));
    }
}
