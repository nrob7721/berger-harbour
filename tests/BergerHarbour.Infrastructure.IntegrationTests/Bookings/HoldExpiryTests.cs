using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.IntegrationTests.Bookings;

/// <summary>A lapsed hold stops blocking its dates and the job marks it Expired.</summary>
[Collection(EmulatorCollection.Name)]
public class HoldExpiryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task Hold_expiry_releases_the_dates()
    {
        var boat = await fx.BoatAsync("pacific-blue");
        var hold = Booking.CreateOnlineHold(BookingId.New(), new OnlineHold(boat.Id, Domain.BookingDrafts.BookingDraftId.New(), PeriodType.Midweek,
            StayRange.Create(EmulatorFixture.D("2029-03-05"), EmulatorFixture.D("2029-03-09")), 2, 0,
            new CustomerDetails("Jane", "Smith", EmailAddress.Create("hold@example.com"), MobileNumber.Create("0412345678")), [], 4060, 1000,
            PaymentScheduleType.Standard, null, fx.Clock.UtcNow, fx.Clock.UtcNow.AddMinutes(40), "hash-hold"), fx.Clock.UtcNow);
        await fx.Get<Application.Abstractions.IBoatScheduleLock>().RunExclusiveAsync(boat.Id, session =>
        {
            session.SaveBooking(hold);
            return Task.FromResult(true);
        });
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2029-03-06", "2029-03-08", email: "blocked@example.com")));

        fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(41);
        try
        {
            await fx.Get<NotificationJobService>().RunAsync();
            Assert.Equal(BookingStatus.Expired, (await fx.Get<IBookingRepository>().GetAsync(hold.Id))!.Status);
            await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2029-03-06", "2029-03-08", email: "free@example.com"));
        }
        finally
        {
            fx.Clock.UtcNow = fx.Clock.UtcNow.AddMinutes(-41);
        }
    }
}
