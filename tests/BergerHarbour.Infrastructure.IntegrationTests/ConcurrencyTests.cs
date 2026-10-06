using BergerHarbour.Application.Customers;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore;

namespace BergerHarbour.Infrastructure.IntegrationTests;

public class ConcurrencyTests : IClassFixture<EmulatorFixture>
{
    private readonly EmulatorFixture _fx;

    public ConcurrencyTests(EmulatorFixture fx) => _fx = fx;

    private async Task<string> TryBookAsync(BoatId boat, DateOnly start, int nights, DateTimeOffset now)
    {
        try
        {
            await new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(boat, async session =>
            {
                var stay = new Stay(start, start.AddDays(nights));
                BookingAvailabilityService.EnsureAvailable(boat, stay, await session.GetOccupyingBookingsAsync(stay),
                    await session.GetUnavailabilitiesAsync(stay), now);
                var booking = Make.StaffBooking(boat, start, nights, now: now);
                session.Save(booking);
                return booking;
            });
            return "ok";
        }
        catch (AvailabilityConflictException ex)
        {
            return ex.Message;
        }
    }

    [Fact]
    public async Task Concurrent_overlapping_bookings_on_one_boat_have_exactly_one_winner()
    {
        var boat = BoatId.New();
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(2027, 3, 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => TryBookAsync(boat, start.AddDays(i % 2), 4, now)));

        Assert.Single(results, r => r == "ok");
        Assert.All(results.Where(r => r != "ok"), r => Assert.Equal(AvailabilityConflictException.DatesNoLongerAvailable, r));
        var stored = await new FirestoreBookingRepository(_fx.Db).ListOverlappingAsync(start, start.AddDays(10), null, boat);
        Assert.Single(stored);
    }

    [Fact]
    public async Task Concurrent_bookings_on_different_boats_all_succeed()
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(2027, 3, 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TryBookAsync(BoatId.New(), start, 4, now)));
        Assert.All(results, r => Assert.Equal("ok", r));
    }

    [Fact]
    public async Task Concurrent_unavailability_and_booking_cannot_both_win()
    {
        var boat = BoatId.New();
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(2027, 4, 5);
        var unavailability = Task.Run(async () =>
        {
            try
            {
                await new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(boat, async session =>
                {
                    var nights = new NightRange(start.AddDays(1), start.AddDays(2));
                    BookingAvailabilityService.EnsureUnavailabilityAllowed(boat, nights,
                        await session.GetOccupyingBookingsAsync(nights.ToStay()), now);
                    session.Save(BoatUnavailability.Create(boat, nights.FirstNight, nights.LastNight, null, now));
                    return 0;
                });
                return "ok";
            }
            catch (AvailabilityConflictException)
            {
                return "conflict";
            }
        });
        var booking = TryBookAsync(boat, start, 4, now);
        var results = new[] { await unavailability, await booking };
        Assert.Single(results, r => r == "ok");
    }

    [Fact]
    public async Task Expired_hold_releases_the_dates()
    {
        var boat = BoatId.New();
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(2027, 5, 3);
        var hold = Booking.CreateOnlineHold(BookingReference.Create(2026, 7001), CustomerId.New(), boat, PeriodType.Midweek,
            new Stay(start, start.AddDays(4)), 2, 4060m, 1000m, PaymentSchedule.Standard, [], now.AddMinutes(40), null, now,
            "h1", now);
        await new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(boat, s =>
        {
            s.Save(hold);
            return Task.FromResult(0);
        });

        Assert.NotEqual("ok", await TryBookAsync(boat, start, 4, now));
        Assert.Equal("ok", await TryBookAsync(boat, start, 4, now.AddMinutes(41)));
    }

    [Fact]
    public async Task Stale_version_is_rejected_by_the_repository()
    {
        var repo = new FirestoreBookingRepository(_fx.Db);
        var booking = Make.StaffBooking(BoatId.New(), new DateOnly(2027, 6, 7));
        await new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(booking.BoatId, s =>
        {
            s.Save(booking);
            return Task.FromResult(0);
        });

        var first = (await repo.GetAsync(booking.Id))!;
        var second = (await repo.GetAsync(booking.Id))!;
        first.UpdateComments("first", DateTimeOffset.UtcNow);
        await repo.SaveAsync(first);
        Assert.Equal(2, first.Version);

        second.UpdateComments("second", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repo.SaveAsync(second));
        Assert.Equal("first", (await repo.GetAsync(booking.Id))!.Comments);
    }

    [Fact]
    public async Task Stale_version_is_rejected_inside_the_exclusive_section()
    {
        var repo = new FirestoreBookingRepository(_fx.Db);
        var booking = Make.StaffBooking(BoatId.New(), new DateOnly(2027, 6, 14));
        var scheduleLock = new FirestoreBoatScheduleLock(_fx.Db);
        await scheduleLock.RunExclusiveAsync(booking.BoatId, s =>
        {
            s.Save(booking);
            return Task.FromResult(0);
        });
        var stale = (await repo.GetAsync(booking.Id))!;
        var fresh = (await repo.GetAsync(booking.Id))!;
        fresh.Cancel(DateTimeOffset.UtcNow);
        await repo.SaveAsync(fresh);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => scheduleLock.RunExclusiveAsync(booking.BoatId, async s =>
        {
            var loaded = (await s.GetBookingAsync(booking.Id))!;
            loaded.EnsureVersion(stale.Version);
            return 0;
        }));
    }

    [Fact]
    public async Task Customer_email_stays_unique_under_concurrency()
    {
        var repo = new FirestoreCustomerRepository(_fx.Db);
        var service = new CustomerService(repo, new FixedClock(DateTimeOffset.UtcNow));
        var email = $"race-{Guid.NewGuid():N}@example.com";
        var customers = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            service.UpsertAsync($"Racer {i}", i % 2 == 0 ? email : email.ToUpperInvariant(), null)));

        Assert.Single(customers.Select(c => c.Id).Distinct());
        Assert.Single(await repo.ListAsync(), c => c.Email.Value == email);
    }
}
