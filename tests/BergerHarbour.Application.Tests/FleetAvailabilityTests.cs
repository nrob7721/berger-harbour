using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

/// <summary>The "Dates &amp; boat" step: the fleet, fleet availability, period availability and changing boat.</summary>
public class FleetAvailabilityTests
{
    private static async Task<TestApp> AppWithInactiveRunawayBearAsync()
    {
        var app = await TestApp.CreateAsync();
        var repository = app.Get<IBoatRepository>();
        var boat = (await repository.GetBySlugAsync("runaway-bear"))!;
        boat.Deactivate(app.Clock.UtcNow);
        await repository.SaveAsync(boat);
        return app;
    }

    private static async Task<Booking> StaffBookingAsync(TestApp app, string slug, DateOnly start, bool standby = false,
        int sequence = 900)
    {
        var booking = Booking.CreateByStaff(BookingReference.Create(start.Year, sequence), CustomerId.New(), new BoatId(slug),
            PeriodType.Midweek, new Stay(start, start.AddDays(4)), 2, 0, Stay.CheckInTime, Stay.CheckOutTime, standby, null,
            2000m, 500m, PaymentSchedule.Standard, "hash-" + sequence, app.Clock.UtcNow);
        await app.Get<IBookingRepository>().SaveAsync(booking);
        return booking;
    }

    [Fact]
    public async Task The_fleet_lists_active_house_boats_largest_first_with_settings()
    {
        var app = await AppWithInactiveRunawayBearAsync();
        var fleet = await app.Get<OnlineBookingService>().GetFleetAsync();

        Assert.DoesNotContain(fleet.Boats, b => b.Slug == "runaway-bear");
        Assert.Equal(13, fleet.Boats.Count);
        Assert.Equal(["blue-bayou", "gypsea-belle", "image-anne"], fleet.Boats.Take(3).Select(b => b.Slug));
        Assert.Equal("wavebreak", fleet.Boats[^1].Slug);
        var pacificBlue = fleet.Boats.Single(b => b.Slug == "pacific-blue");
        Assert.Equal(("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", 12, true, false),
            (pacificBlue.ImageUrl, pacificBlue.MaxNoOfGuests, pacificBlue.HasSpa, pacificBlue.IsPetFriendly));
        Assert.True(fleet.Boats.Single(b => b.Slug == "wanderer").IsPetFriendly);
        Assert.Equal(("11am", "2pm", "8am"), (fleet.Settings.CheckInTime, fleet.Settings.CheckInWindowEnd, fleet.Settings.CheckOutTime));
        Assert.Equal(30, fleet.Settings.MinimumLeadTimeDays);
    }

    [Fact]
    public async Task One_boat_is_returned_active_or_not()
    {
        var app = await AppWithInactiveRunawayBearAsync();
        var service = app.Get<OnlineBookingService>();
        var inactive = await service.GetBoatAsync("runaway-bear");
        Assert.False(inactive.IsActive);
        Assert.Empty(inactive.Addons);
        Assert.Equal(2, inactive.MaxNoOfGuests);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetBoatAsync("no-such-boat"));
    }

    [Fact]
    public async Task Fleet_availability_has_each_active_boats_blocking_ranges_only()
    {
        var app = await AppWithInactiveRunawayBearAsync();
        var start = app.MondayAfter(40);
        await StaffBookingAsync(app, "pacific-blue", start, sequence: 901);
        await StaffBookingAsync(app, "kalinda", start, standby: true, sequence: 902);
        var cancelled = await StaffBookingAsync(app, "kalinda", start.AddDays(7), sequence: 903);
        cancelled.Cancel(app.Clock.UtcNow);
        await app.Get<IBookingRepository>().SaveAsync(cancelled);
        await StaffBookingAsync(app, "runaway-bear", start, sequence: 904);
        await app.Get<FleetService>().CreateUnavailabilityAsync("blue-bayou",
            new UnavailabilityRequest(0, start.AddDays(1), start.AddDays(2), "Maintenance"));

        var result = await app.Get<OnlineBookingService>().GetFleetAvailabilityAsync(start.AddDays(-7), start.AddDays(30));

        Assert.Equal(13, result.Boats.Count);
        Assert.DoesNotContain(result.Boats, b => b.Slug == "runaway-bear");
        Assert.Equal([new DateRangeDto(start, start.AddDays(4))], Unavailable("pacific-blue"));
        Assert.Equal([new DateRangeDto(start.AddDays(1), start.AddDays(3))], Unavailable("blue-bayou"));
        Assert.Empty(Unavailable("kalinda")); // a stand-by and a cancelled booking don't block
        Assert.Empty(result.EnquireOnly);
        return;

        IReadOnlyList<DateRangeDto> Unavailable(string slug) => result.Boats.Single(b => b.Slug == slug).Unavailable;
    }

    [Fact]
    public async Task Fleet_availability_includes_blocked_periods_and_limits_the_range()
    {
        var app = await TestApp.CreateAsync();
        var service = app.Get<OnlineBookingService>();
        var result = await service.GetFleetAvailabilityAsync(new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 15));
        Assert.Contains(result.EnquireOnly, p => p.FirstNight <= new DateOnly(2026, 12, 25) && p.LastNight >= new DateOnly(2026, 12, 25));

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.GetFleetAvailabilityAsync(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1)));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.GetFleetAvailabilityAsync(new DateOnly(2026, 12, 1), new DateOnly(2028, 4, 1)));
    }

    [Fact]
    public async Task Period_availability_gives_each_boats_availability_and_hire_price()
    {
        var app = await AppWithInactiveRunawayBearAsync();
        var start = app.MondayAfter(40);
        await StaffBookingAsync(app, "pacific-blue", start, sequence: 911);
        await StaffBookingAsync(app, "kalinda", start, standby: true, sequence: 912);
        await app.Get<FleetService>().CreateUnavailabilityAsync("blue-bayou",
            new UnavailabilityRequest(0, start.AddDays(3), start.AddDays(3), null));

        // An expired hold doesn't block.
        var draftId = await app.DraftAsync(start, slug: "ocean-spirit");
        await app.Get<OnlineBookingService>().CreateAsync(new CreateOnlineBookingRequest(draftId, "ocean-spirit",
            PeriodType.Midweek, start, 4, 2, "Jane Citizen", "jane@example.com", "0412 345 678", [], false, null, true));
        Assert.False((await Period()).Single(b => b.Slug == "ocean-spirit").Available);
        app.Clock.Advance(TimeSpan.FromHours(1));

        var boats = await Period();
        Assert.Equal(13, boats.Count);
        Assert.DoesNotContain(boats, b => b.Slug == "runaway-bear");
        Assert.Equal(new BoatPeriodDto("pacific-blue", false, 4060m), boats.Single(b => b.Slug == "pacific-blue"));
        Assert.False(boats.Single(b => b.Slug == "blue-bayou").Available);
        Assert.True(boats.Single(b => b.Slug == "kalinda").Available);
        Assert.True(boats.Single(b => b.Slug == "ocean-spirit").Available);
        var quote = await app.Get<OnlineBookingService>().QuoteAsync("wanderer", new QuoteRequest(PeriodType.Midweek, start, 2, 0, []));
        Assert.Equal(new BoatPeriodDto("wanderer", true, quote.HirePrice), boats.Single(b => b.Slug == "wanderer"));
        return;

        async Task<IReadOnlyList<BoatPeriodDto>> Period() =>
            (await app.Get<OnlineBookingService>().GetPeriodAvailabilityAsync(PeriodType.Midweek, start)).Boats;
    }

    [Fact]
    public async Task Period_availability_rejects_stays_that_cannot_be_booked_online()
    {
        var app = await TestApp.CreateAsync();
        var service = app.Get<OnlineBookingService>();
        var monday = app.MondayAfter(40);

        async Task<IReadOnlyDictionary<string, string[]>> Errors(PeriodType type, DateOnly start) =>
            (await Assert.ThrowsAsync<RequestValidationException>(() => service.GetPeriodAvailabilityAsync(type, start))).Errors;

        Assert.True((await Errors(PeriodType.Midweek, monday.AddDays(1))).ContainsKey("startDate")); // shape
        Assert.True((await Errors(PeriodType.Custom, monday)).ContainsKey("periodType"));
        Assert.True((await Errors(PeriodType.Midweek, app.MondayAfter(7))).ContainsKey("startDate")); // lead time
        Assert.True((await Errors(PeriodType.Week, new DateOnly(2027, 12, 6))).ContainsKey("startDate")); // open until
        Assert.True((await Errors(PeriodType.Midweek, new DateOnly(2026, 12, 21))).ContainsKey("startDate")); // Christmas
    }

    [Fact]
    public async Task A_draft_can_move_to_another_boat_and_is_then_booked_on_it()
    {
        var app = await TestApp.CreateAsync();
        var service = app.Get<OnlineBookingService>();
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);
        var draft = BookingDraft.FromSnapshot(Assert.Single(app.Store.BookingDrafts.All()));
        Assert.Equal((TestApp.PacificBlue, 4, 2), (draft.BoatId, draft.NumberOfAdults, draft.NumberOfChildren));

        await service.UpdateDraftAsync(draftId, new UpdateBookingDraftRequest("kalinda", PeriodType.Midweek, start, 3, 1,
            "Jane Citizen", "jane@example.com", "0412 345 678", [], BookingStep.Addons));
        draft = BookingDraft.FromSnapshot(Assert.Single(app.Store.BookingDrafts.All()));
        Assert.Equal((new BoatId("kalinda"), 3, 1), (draft.BoatId, draft.NumberOfAdults, draft.NumberOfChildren));

        // The draft no longer matches the boat it started on…
        var stale = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(new CreateOnlineBookingRequest(
            draftId, "pacific-blue", PeriodType.Midweek, start, 3, 1, "Jane Citizen", "jane@example.com", "0412 345 678", [],
            false, null, true)));
        Assert.True(stale.Errors.ContainsKey("draftId"));

        // …and is accepted for the boat it moved to.
        var result = await service.CreateAsync(new CreateOnlineBookingRequest(draftId, "kalinda", PeriodType.Midweek, start, 3, 1,
            "Jane Citizen", "jane@example.com", "0412 345 678", [], false, null, true));
        Assert.Equal(new BoatId("kalinda"), (await app.BookingAsync(result.Reference)).BoatId);
    }

    [Fact]
    public async Task Drafts_cannot_move_to_an_inactive_boat_and_need_an_adult()
    {
        var app = await AppWithInactiveRunawayBearAsync();
        var service = app.Get<OnlineBookingService>();
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);

        var inactive = await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateDraftAsync(draftId,
            new UpdateBookingDraftRequest("runaway-bear", PeriodType.Midweek, start, 2, 0, "Jane", "jane@example.com",
                "0412 345 678", [], BookingStep.Addons)));
        Assert.True(inactive.Errors.ContainsKey("slug"));

        var noAdult = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDraftAsync(
            new CreateBookingDraftRequest("pacific-blue", PeriodType.Midweek, start, 0, 2, "Jane", "jane@example.com",
                "0412 345 678", "ok"), null));
        Assert.True(noAdult.Errors.ContainsKey("numberOfAdults"));
    }
}
