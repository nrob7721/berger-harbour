using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using static BergerHarbour.Infrastructure.IntegrationTests.EmulatorFixture;

namespace BergerHarbour.Infrastructure.IntegrationTests;

[Collection(EmulatorCollection.Name)]
public class RepositoryRoundTripTests(EmulatorFixture f)
{
    [Fact]
    public async Task Seeded_reference_data_round_trips()
    {
        var boats = await f.Get<IBoatRepository>().ListAsync();
        Assert.Equal(14, boats.Count);
        var pb = boats.Single(b => b.Slug == "pacific-blue");
        Assert.True(pb.IsActive);
        Assert.Equal(12, pb.Rates.Count);
        Assert.Equal(4060m, pb.RateFor(Season.NormalId, PeriodType.Midweek));
        Assert.Equal(250m, boats.Single(b => b.Slug == "runaway-bear").DepositAmount);
        var addons = await f.Get<IAddonDefinitionRepository>().ListAsync();
        Assert.Equal("yabby-pumps", addons[0].Id);
        Assert.Equal(new AddonPrices(95, 95, 130), addons.Single(a => a.Id == "outboard-motor").Prices);
        Assert.Equal("car spots", addons.Single(a => a.Id == "car-parking").CapacityUnitLabel);
        var seasons = await f.Get<ISeasonRepository>().ListAsync();
        Assert.True(seasons.Single(s => s.Id == "peak").Ranges[0].Wraps);
        Assert.Equal(8, (await f.Get<IEmailTemplateRepository>().ListAsync()).Count);
        var settings = await f.Get<IBusinessSettingsRepository>().GetAsync();
        Assert.Equal(new DateOnly(2027, 11, 30), settings!.BookingsOpenUntil);
        Assert.Single(await f.Get<IBlockedPeriodRepository>().ListAsync());
    }

    [Fact]
    public async Task Booking_round_trips_every_field()
    {
        var repo = f.Get<IBookingRepository>();
        var hold = Booking.CreateOnlineHold(new BoatId("rt-boat"), PeriodType.Weekend, new StayRange(D("2027-02-05"), D("2027-02-08")),
            new GuestNumbers(3, 2), new CustomerDetails("Jane", "Müller", "jane@example.com", "+61412345678"), "draft-x", 4060.5m, 1000,
            PaymentScheduleType.Extended, [AddonLine.Create(null, "ice", "Ice", 3, 5.25m), AddonLine.Create(null, "hamper", "Hamper", 1, null)],
            new RoomingWarningAcceptance(DateTimeOffset.UtcNow, "warning"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(40),
            Ids.New(), "hash-x", DateTimeOffset.UtcNow);
        await repo.SaveAsync(hold);
        hold.AttachCheckoutSession("cs_1");
        hold.RecordPayment(new Payment("p1", 1000, PaymentMethod.Stripe, "pi_1", DateTimeOffset.UtcNow, "note", BookingCreator.Customer));
        hold.MarkNotificationSent("Key", DateTimeOffset.UtcNow);
        await repo.SaveAsync(hold);

        var loaded = (await repo.GetAsync(hold.Id))!;
        Assert.Equal(2, loaded.Version);
        Assert.Equal(hold.Stay, loaded.Stay);
        Assert.Equal(4060.5m, loaded.HirePrice);
        Assert.Equal(5.25m, loaded.AddonLines[0].UnitPrice);
        Assert.Null(loaded.AddonLines[1].UnitPrice);
        Assert.Equal("Müller", loaded.CustomerDetails!.LastName);
        Assert.Equal("cs_1", loaded.StripeCheckoutSessionId);
        Assert.Equal("pi_1", loaded.Payments.Single().StripePaymentIntentId);
        Assert.True(loaded.HasSentNotification("Key"));
        Assert.Equal(PaymentScheduleType.Extended, loaded.PaymentSchedule);
        Assert.Equal("warning", loaded.RoomingWarningAcceptance!.WarningTextShown);
        Assert.Equal(new TimeOnly(11, 0), loaded.CheckInTime);
        Assert.Equal(loaded.Id, (await repo.GetByPaymentLinkHashAsync("hash-x"))!.Id);
    }

    [Fact]
    public async Task Customer_draft_and_unavailability_round_trip()
    {
        var customers = f.Get<ICustomerRepository>();
        var c = Customer.Create("Ann", "Lee", "ann.rt@example.com", "0412345678", DateTimeOffset.UtcNow);
        c.UpdateComments("notes");
        c.UpdateContactDetails("Ann", "Lee", "0412345678", null, "1 River Rd\nMannum SA 5238");
        await customers.SaveAsync(c);
        var byEmail = (await customers.GetByEmailAsync("ann.rt@example.com"))!;
        Assert.Equal(c.Id, byEmail.Id);
        Assert.Equal("notes", byEmail.Comments);
        Assert.Equal("1 River Rd\nMannum SA 5238", byEmail.Address);

        var drafts = f.Get<IBookingDraftRepository>();
        var d = BookingDraft.Create(new DraftDetails(new BoatId("b"), PeriodType.Midweek, new StayRange(D("2027-03-08"), D("2027-03-12")),
            new GuestNumbers(2, 1), "Ann", "Lee", "ann.rt@example.com", "0412345678", [new AddonRequest("ice", 2)], DraftStep.Addons), DateTimeOffset.UtcNow);
        await drafts.SaveAsync(d);
        var loaded = (await drafts.GetAsync(d.Id))!;
        Assert.Equal(DraftStep.Addons, loaded.LastStepReached);
        Assert.Equal(2, loaded.AddonRequests.Single().Quantity);
        Assert.Equal(d.Id, (await drafts.GetLatestForEmailAsync("ann.rt@example.com"))!.Id);

        var boat = await f.BoatAsync("kalinda");
        var u = BoatUnavailability.Create(boat.Id, D("2027-06-01"), D("2027-06-03"), "Paint", DateTimeOffset.UtcNow);
        await f.Get<IBoatScheduleLock>().RunExclusiveAsync(boat.Id, s => { s.SaveUnavailability(u); return Task.FromResult(0); });
        var listed = await f.Get<IBoatUnavailabilityRepository>().ListInRangeAsync(D("2027-06-03"), D("2027-06-10"));
        Assert.Contains(listed, x => x.Id == u.Id);
        Assert.DoesNotContain(await f.Get<IBoatUnavailabilityRepository>().ListInRangeAsync(D("2027-06-04"), D("2027-06-10")), x => x.Id == u.Id);
    }

    [Fact]
    public async Task Optimistic_concurrency_rejects_a_stale_save()
    {
        var repo = f.Get<IBoatRepository>();
        var a = (await repo.GetBySlugAsync("wanderer"))!;
        var b = (await repo.GetBySlugAsync("wanderer"))!;
        a.Deactivate();
        await repo.SaveAsync(a);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repo.SaveAsync(b));
    }
}

[Collection(EmulatorCollection.Name)]
public class ConcurrencyTests(EmulatorFixture f)
{
    [Fact]
    public async Task Concurrent_bookings_for_the_same_boat_and_dates_have_exactly_one_winner()
    {
        await f.ActivateAsync("gypsea-belle");
        var requests = await Task.WhenAll(
            f.StaffRequestAsync("2027-08-02", "2027-08-06", "gypsea-belle", "race1@example.com"),
            f.StaffRequestAsync("2027-08-04", "2027-08-09", "gypsea-belle", "race2@example.com"));
        var results = await Task.WhenAll(requests.Select(async r =>
        {
            try { await f.Admin().CreateAsync(r); return "ok"; }
            catch (ConflictException e) { return e.Message; }
        }));
        Assert.Single(results, r => r == "ok");
        Assert.Single(results, r => r == BookingAvailabilityService.TakenMessage);
    }

    [Fact]
    public async Task Customer_email_stays_unique_under_concurrency()
    {
        var repo = f.Get<ICustomerRepository>();
        var attempts = Enumerable.Range(0, 2).Select(i => Customer.Create("Sam", "Same", "same@example.com", null, DateTimeOffset.UtcNow)).ToList();
        var results = await Task.WhenAll(attempts.Select(async c =>
        {
            try { await repo.SaveAsync(c); return true; }
            catch (Exception e) when (e is ConflictException or Grpc.Core.RpcException) { return false; }
        }));
        Assert.Single(results, r => r);
        Assert.Single(await repo.ListAsync(), c => c.Email == "same@example.com");
    }

    [Fact]
    public async Task Reference_counter_gives_distinct_suffixes_under_concurrency()
    {
        var generator = f.Get<IBookingReferenceGenerator>();
        var suffixes = await Task.WhenAll(generator.NextSuffixAsync("RACE-270801"), generator.NextSuffixAsync("RACE-270801"));
        Assert.Equal([0, 1], suffixes.Order());
    }

    [Fact]
    public async Task Expired_hold_releases_its_dates()
    {
        var boat = await f.ActivateAsync("image-anne");
        var now = DateTimeOffset.UtcNow;
        var stay = new StayRange(D("2027-09-06"), D("2027-09-10"));
        var hold = Booking.CreateOnlineHold(boat.Id, PeriodType.Midweek, stay, new GuestNumbers(2, 0),
            new CustomerDetails("A", "B", "ab@example.com", "+61412345678"), "d", 2100, 1000, PaymentScheduleType.Standard, [], null, now, now.AddMinutes(40),
            Ids.New(), "h-" + Ids.New(), now);
        var schedule = f.Get<IBoatScheduleLock>();
        await schedule.RunExclusiveAsync(boat.Id, s => { s.SaveBooking(hold); return Task.FromResult(0); });
        var request = await f.StaffRequestAsync("2027-09-06", "2027-09-10", "image-anne", "after@example.com");
        await Assert.ThrowsAsync<ConflictException>(() => f.Admin().CreateAsync(request));
        hold.Expire();
        await f.Get<IBookingRepository>().SaveAsync(hold);
        var created = await f.Admin().CreateAsync(request);
        Assert.Equal(BookingStatus.Active, created.Booking.Status);
    }
}

[Collection(EmulatorCollection.Name)]
public class QueryTests(EmulatorFixture f)
{
    [Fact]
    public async Task Addon_capacity_counts_lines_across_boats()
    {
        await f.ActivateAsync("blue-bayou");
        await f.ActivateAsync("paradise-ii");
        await f.Admin().CreateAsync(await f.StaffRequestAsync("2027-10-12", "2027-10-15", "blue-bayou", "cap1@example.com", [new(null, "car-parking", 3, null)]));
        await f.Admin().CreateAsync(await f.StaffRequestAsync("2027-10-14", "2027-10-19", "paradise-ii", "cap2@example.com", [new(null, "car-parking", 2, null)]));
        var catalogue = f.Get<PublicCatalogueService>();
        Assert.Equal(6, (await catalogue.GetAddonRemainingAsync("car-parking", D("2027-10-12"), D("2027-10-15"))).Remaining);
        Assert.Equal(8, (await catalogue.GetAddonRemainingAsync("car-parking", D("2027-10-12"), D("2027-10-14"))).Remaining);
        Assert.Equal(9, (await catalogue.GetAddonRemainingAsync("car-parking", D("2027-10-15"), D("2027-10-18"))).Remaining);
    }

    [Fact]
    public async Task Fleet_availability_spans_boats()
    {
        await f.ActivateAsync("jfs-rhyanna");
        await f.ActivateAsync("marie-claire");
        await f.Admin().CreateAsync(await f.StaffRequestAsync("2027-11-01", "2027-11-05", "jfs-rhyanna", "fa1@example.com"));
        await f.Admin().CreateAsync(await f.StaffRequestAsync("2027-11-08", "2027-11-12", "marie-claire", "fa2@example.com"));
        var r = await f.Get<PublicCatalogueService>().GetAvailabilityAsync(D("2027-11-01"), D("2027-12-01"));
        Assert.Contains(new StayRangeDto(D("2027-11-01"), D("2027-11-05")), r.Boats.Single(b => b.Slug == "jfs-rhyanna").Unavailable);
        Assert.Contains(new StayRangeDto(D("2027-11-08"), D("2027-11-12")), r.Boats.Single(b => b.Slug == "marie-claire").Unavailable);
        var period = await f.Get<PublicCatalogueService>().GetPeriodAvailabilityAsync(PeriodType.Midweek, D("2027-11-01"));
        Assert.False(period.Boats.Single(b => b.Slug == "jfs-rhyanna").Available);
        Assert.True(period.Boats.Single(b => b.Slug == "marie-claire").Available);
    }

    [Fact]
    public async Task Timeline_and_search_queries_work_on_firestore()
    {
        await f.Admin().CreateAsync(await f.StaffRequestAsync("2027-07-05", "2027-07-09", "pacific-blue", "tl@example.com"));
        var timeline = await f.Admin().GetTimelineAsync(D("2027-07-01"), D("2027-08-01"), true);
        Assert.Contains(timeline.Bookings, b => b.StartDate == D("2027-07-05"));
        Assert.NotEmpty(await f.Admin().SearchAsync("tl@example"));
        Assert.NotEmpty(await f.Admin().SearchAsync("smith-270705"));
    }
}
