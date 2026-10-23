using BergerHarbour.Domain.BookingDrafts;
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
        var booking = Booking.CreateOnlineHold(BookingReference.Create(2026, 143),
            new BookingCustomerDetails("Jane Citizen", "jane@example.com", "+44 7911 123456"), BookingDraftId.New(), boat,
            PeriodType.Midweek, new Stay(new DateOnly(2026, 12, 7), new DateOnly(2026, 12, 11)), 7, 2, 4060.50m, 1000m,
            PaymentSchedule.Standard,
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
        Assert.Null(loaded.CustomerId);
        Assert.Equal("+447911123456", loaded.CustomerDetails!.MobileNumber);

        loaded.Activate(Now);
        loaded.ChangeCustomer(CustomerId.New(), Now);
        loaded.ChangeDetails(boat, PeriodType.Custom, loaded.Stay, 7, 2, new TimeOnly(9, 30), new TimeOnly(10, 15), false, null,
            loaded.HirePrice, PaymentSchedule.Standard, Now);
        await repo.SaveAsync(loaded);
        booking = (await repo.GetAsync(booking.Id))!;
        Assert.Equal((new TimeOnly(9, 30), new TimeOnly(10, 15)), (booking.CheckInTime, booking.CheckOutTime));
        Assert.Equal(booking.Id, (await repo.GetByReferenceAsync(BookingReference.Parse("BH-260143")))!.Id);
        Assert.Equal(booking.Id, (await repo.GetByPaymentLinkTokenHashAsync("tokenhash"))!.Id);
        Assert.Single(await repo.ListOverlappingAsync(new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 20),
            [BookingStatus.Active], boat));
        Assert.Empty(await repo.ListOverlappingAsync(new DateOnly(2026, 12, 11), new DateOnly(2026, 12, 20), null, boat));
        Assert.Single(await repo.ListByCustomerAsync([booking.CustomerId!.Value]));
    }

    [Fact]
    public async Task Capacity_query_finds_bookings_with_the_add_on_across_boats()
    {
        var repo = new FirestoreBookingRepository(_fx.Db);
        var parking = new AddonId("car-parking-" + Guid.NewGuid().ToString("N")[..8]);
        var start = new DateOnly(2027, 4, 5);
        async Task<Booking> Save(BoatId boat, DateOnly from, int nights, int spots)
        {
            var b = Make.StaffBooking(boat, from, nights);
            b.AddAddon(new RequestedAddon(parking, "Car parking", spots, null), Now);
            b.AddAddon(new RequestedAddon(new AddonId("ice"), "Ice", 1, null), Now);
            await LockSaveAsync(b);
            return b;
        }

        await Save(BoatId.New(), start, 3, 3);
        await Save(BoatId.New(), start.AddDays(2), 5, 2);
        var cancelled = await Save(BoatId.New(), start, 3, 4);
        cancelled.Cancel(Now);
        await repo.SaveAsync(cancelled);
        await Save(BoatId.New(), start.AddDays(10), 3, 5); // no overlap

        var found = await repo.ListWithAddonOverlappingAsync(parking, start, start.AddDays(3));
        Assert.Equal(2, found.Count);
        Assert.Equal(6, AddonCapacityService.Remaining(
            AddonDefinition.CreateWithId(parking, "Car parking", null, true, null, true, 11, "car spots", 0),
            new Stay(start, start.AddDays(3)), found, Now));
        Assert.Empty(await repo.ListWithAddonOverlappingAsync(parking, start.AddDays(-5), start));
    }

    [Fact]
    public async Task Fleet_availability_query_finds_active_and_pending_bookings_across_boats()
    {
        var repo = new FirestoreBookingRepository(_fx.Db);
        var start = new DateOnly(2027, 6, 7);
        async Task<Booking> Save(DateOnly from, int nights)
        {
            var b = Make.StaffBooking(BoatId.New(), from, nights);
            await LockSaveAsync(b);
            return b;
        }

        var first = await Save(start, 4);
        var second = await Save(start.AddDays(2), 5);
        var cancelled = await Save(start, 4);
        cancelled.Cancel(Now);
        await repo.SaveAsync(cancelled);
        var before = await Save(start.AddDays(-4), 4); // checks out on the first day of the range
        var after = await Save(start.AddDays(10), 3);

        var found = (await repo.ListOverlappingAsync(start, start.AddDays(7),
            [BookingStatus.Active, BookingStatus.PendingPayment])).Select(b => b.Id).ToList();
        Assert.Contains(first.Id, found);
        Assert.Contains(second.Id, found);
        Assert.DoesNotContain(cancelled.Id, found);
        Assert.DoesNotContain(before.Id, found);
        Assert.DoesNotContain(after.Id, found);
    }

    [Fact]
    public async Task Booking_draft_round_trips_and_is_queried_by_email_activity_and_age()
    {
        var repo = new FirestoreBookingDraftRepository(_fx.Db);
        var email = $"draft-{Guid.NewGuid():N}@example.com";
        var boat = BoatId.New();
        var stay = new Stay(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 5));
        var draft = BookingDraft.Create(boat, PeriodType.Midweek, stay, 2, 0, "Drafty", email, "0412 345 678", Now);
        draft.Update(BoatId.New(), PeriodType.Midweek, stay, 4, 1, "Drafty", email, "0412 345 678", [new DraftAddonRequest(new AddonId("ice"), 2)],
            BookingStep.Addons, Now.AddMinutes(5));
        await repo.SaveAsync(draft);
        var later = BookingDraft.Create(boat, PeriodType.Midweek, stay, 2, 0, "Drafty", email.ToUpperInvariant(), "0412 345 678", Now.AddHours(1));
        await repo.SaveAsync(later);

        var loaded = await repo.GetAsync(draft.Id);
        Assert.Equivalent(draft.ToSnapshot(), loaded!.ToSnapshot());
        Assert.Equal([later.Id, draft.Id], (await repo.ListByEmailAsync(EmailAddress.Create(email))).Select(d => d.Id));
        Assert.Contains(await repo.ListInactiveSinceAsync(Now.AddMinutes(6)), d => d.Id == draft.Id);
        Assert.DoesNotContain(await repo.ListInactiveSinceAsync(Now.AddMinutes(6)), d => d.Id == later.Id);
        Assert.Contains(await repo.ListCreatedBeforeAsync(Now.AddMinutes(1)), d => d.Id == draft.Id);

        loaded.MarkConverted(BookingId.New(), Now.AddMinutes(10));
        loaded.MarkAbandonedNotified(Now.AddMinutes(11));
        await repo.SaveAsync(loaded);
        Assert.Equivalent(loaded.ToSnapshot(), (await repo.GetAsync(draft.Id))!.ToSnapshot());

        await repo.DeleteAsync((await repo.GetAsync(draft.Id))!);
        Assert.Null(await repo.GetAsync(draft.Id));
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
        boat.UpdateDetails("Round Trip", "round-trip", 10, 6, 0, "3 queens", 1500.25m, 750.50m, Now);
        boat.UpdateFeatures("https://bergerhouseboats.com.au/wp-content/uploads/round-trip.jpg", true, true, Now);
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
        Assert.Equal(500m, settings.DepositAmount);
        Assert.Equal(new DateOnly(2027, 11, 30), settings.BookingsOpenUntil);
        var seasons = await new FirestoreSeasonRepository(_fx.Db).ListAsync();
        Assert.Contains(seasons, s => s.Name == "Peak" && s.Ranges.Single().WrapsYearEnd);
        var addons = await new FirestoreAddonDefinitionRepository(_fx.Db).ListAsync();
        Assert.Equal(12, addons.Count);
        var parking = addons.Single(a => a.Name == "Car parking");
        Assert.Equal((11, "car spots", 5, true), (parking.Capacity, parking.CapacityUnitLabel, parking.SortOrder, parking.QuantityApplies));
        Assert.Equal(8, (await new FirestoreEmailTemplateRepository(_fx.Db).ListAsync()).Count);
        var blocked = Assert.Single(await new FirestoreBlockedPeriodRepository(_fx.Db).ListAsync());
        Assert.True(blocked.UsesExtendedPaymentSchedule);
        var pacificBlue = (await new FirestoreBoatRepository(_fx.Db).GetBySlugAsync("pacific-blue"))!;
        Assert.True(pacificBlue.IsActive);
        Assert.Equal((6, 2, 1000m), (pacificBlue.AdultBeds, pacificBlue.KidsBeds, pacificBlue.DepositAmount));
        Assert.Equal(("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", true, false),
            (pacificBlue.ImageUrl, pacificBlue.HasSpa, pacificBlue.IsPetFriendly));
        var kalinda = (await new FirestoreBoatRepository(_fx.Db).GetBySlugAsync("kalinda"))!;
        Assert.True(kalinda.IsActive);
        Assert.Equal((5, 2, (decimal?)null), (kalinda.AdultBeds, kalinda.KidsBeds, kalinda.DepositAmount));

        // Seeding again changes nothing.
        await _fx.SeedAsync(new FixedClock(Now));
        Assert.Equal(3, (await new FirestoreSeasonRepository(_fx.Db).ListAsync()).Count);
    }

    [Fact]
    public async Task Customer_round_trips_and_is_found_by_email()
    {
        var repo = new FirestoreCustomerRepository(_fx.Db);
        var customer = Customer.Create("Round Trip", "Round.Trip@Example.com", "0412 111 222", Now);
        customer.ChangeSecondaryPhone("+64 21 123 4567", Now);
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
