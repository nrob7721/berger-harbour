using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore;

namespace BergerHarbour.Infrastructure.IntegrationTests;

public class RepositoryRoundTripTests : IClassFixture<EmulatorFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 1, 2, 3, TimeSpan.Zero);
    private readonly EmulatorFixture _fx;

    public RepositoryRoundTripTests(EmulatorFixture fx) => _fx = fx;

    [Fact]
    public async Task Booking_with_every_field_round_trips()
    {
        var repo = new FirestoreBookingRepository(_fx.Db);
        var boat = BoatId.New();
        var booking = Booking.CreateOnlineHold(BookingReference.Create(2026, 143), CustomerId.New(), boat, PeriodType.Midweek,
            new Stay(new DateOnly(2026, 12, 7), new DateOnly(2026, 12, 11)), 9, 4060.50m, 1000m, PaymentSchedule.Standard,
            [new RequestedAddon(AddonId.New(), "Ice", 3, null), new RequestedAddon(AddonId.New(), "Outboard motor", 1, 95m)],
            Now.AddMinutes(40), new RoomingWarningAcceptance(Now, "warning text"), Now, "tokenhash", Now);
        booking.AttachCheckoutSession("cs_test_1", Now);
        booking.RecordStripePayment(1000m, "pi_1", Now, Now);
        booking.RecordNotificationSent("BookingConfirmed", Now);

        await LockSaveAsync(booking);
        var loaded = await repo.GetAsync(booking.Id);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.Version);
        Assert.Equivalent(booking.ToSnapshot() with { Version = 1 }, loaded.ToSnapshot());
        Assert.Equal(booking.Id, (await repo.GetByReferenceAsync(BookingReference.Parse("BH-260143")))!.Id);
        Assert.Equal(booking.Id, (await repo.GetByPaymentLinkTokenHashAsync("tokenhash"))!.Id);
        Assert.Single(await repo.ListOverlappingAsync(new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 20),
            [BookingStatus.PendingPayment], boat));
        Assert.Empty(await repo.ListOverlappingAsync(new DateOnly(2026, 12, 11), new DateOnly(2026, 12, 20), null, boat));
        Assert.Single(await repo.ListByCustomerAsync([booking.CustomerId]));
    }

    private Task LockSaveAsync(Booking booking) =>
        new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(booking.BoatId, session =>
        {
            session.Save(booking);
            return Task.FromResult(true);
        });

    [Fact]
    public async Task Boat_round_trips_and_is_found_by_slug()
    {
        var repo = new FirestoreBoatRepository(_fx.Db);
        var seasons = new[] { Season.CreateNormal() };
        var boat = Boat.Create("Round Trip", "round-trip", BoatType.House, 10, Now);
        boat.UpdateDetails("Round Trip", "round-trip", 10, 6, "3 queens", 1500.25m, Now);
        boat.SetRates([.. PeriodTypes.Rated.Select(p => new BoatRate(SeasonId.Normal, p, 1234.56m))], Now);
        boat.SetAllowedAddons([new AddonId("ice")], Now);
        boat.Activate(seasons, Now);
        await repo.SaveAsync(boat);

        var loaded = await repo.GetBySlugAsync("round-trip");
        Assert.Equivalent(boat.ToSnapshot(), loaded!.ToSnapshot());
    }

    [Fact]
    public async Task Settings_aggregates_round_trip()
    {
        await _fx.SeedAsync(new FixedClock(Now));
        var settings = await new FirestoreBusinessSettingsRepository(_fx.Db).GetAsync();
        Assert.Equal(1000m, settings.DepositAmount);
        Assert.Equal(new DateOnly(2027, 11, 30), settings.BookingsOpenUntil);
        var seasons = await new FirestoreSeasonRepository(_fx.Db).ListAsync();
        Assert.Contains(seasons, s => s.Name == "Peak" && s.Ranges.Single().WrapsYearEnd);
        Assert.Equal(12, (await new FirestoreAddonDefinitionRepository(_fx.Db).ListAsync()).Count);
        Assert.Equal(7, (await new FirestoreEmailTemplateRepository(_fx.Db).ListAsync()).Count);
        var blocked = Assert.Single(await new FirestoreBlockedPeriodRepository(_fx.Db).ListAsync());
        Assert.True(blocked.UsesExtendedPaymentSchedule);
        Assert.True((await new FirestoreBoatRepository(_fx.Db).GetBySlugAsync("pacific-blue"))!.IsActive);

        // Seeding again changes nothing.
        await _fx.SeedAsync(new FixedClock(Now));
        Assert.Equal(3, (await new FirestoreSeasonRepository(_fx.Db).ListAsync()).Count);
    }

    [Fact]
    public async Task Customer_round_trips_and_is_found_by_email()
    {
        var repo = new FirestoreCustomerRepository(_fx.Db);
        var customer = Customer.Create("Round Trip", "Round.Trip@Example.com", "0400 111 222", Now);
        await repo.SaveAsync(customer);
        var loaded = await repo.GetByEmailAsync(EmailAddress.Create("round.trip@example.com"));
        Assert.Equivalent(customer.ToSnapshot(), loaded!.ToSnapshot());
        await Assert.ThrowsAsync<DuplicateCustomerEmailException>(() =>
            repo.SaveAsync(Customer.Create("Other", "round.trip@example.com", null, Now)));
    }

    [Fact]
    public async Task Unavailability_round_trips_and_overlap_query_uses_inclusive_nights()
    {
        var boat = BoatId.New();
        var unavailability = BoatUnavailability.Create(boat, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 3), "Slipping", Now);
        await new FirestoreBoatScheduleLock(_fx.Db).RunExclusiveAsync(boat, s =>
        {
            s.Save(unavailability);
            return Task.FromResult(0);
        });

        var repo = new FirestoreBoatUnavailabilityRepository(_fx.Db);
        Assert.Single(await repo.ListOverlappingAsync(new DateOnly(2027, 2, 3), new DateOnly(2027, 2, 10), boat));
        Assert.Empty(await repo.ListOverlappingAsync(new DateOnly(2027, 2, 4), new DateOnly(2027, 2, 10), boat));
        Assert.Empty(await repo.ListOverlappingAsync(new DateOnly(2027, 1, 20), new DateOnly(2027, 2, 1), boat));
        var loaded = Assert.Single(await repo.ListByBoatAsync(boat));
        Assert.Equivalent(unavailability.ToSnapshot(), loaded.ToSnapshot());
        await repo.DeleteAsync(loaded);
        Assert.Empty(await repo.ListByBoatAsync(boat));
    }

    [Fact]
    public async Task Reference_counter_is_sequential_per_year()
    {
        var generator = new FirestoreBookingReferenceGenerator(_fx.Db);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => generator.NextAsync(2031)));
        Assert.Equal(Enumerable.Range(1, 4).Select(n => $"BH-31{n:0000}").Order(), results.Select(r => r.Value).Order());
        Assert.Equal("BH-320001", (await generator.NextAsync(2032)).Value);
    }
}
