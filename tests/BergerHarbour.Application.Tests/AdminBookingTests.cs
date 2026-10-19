using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class AdminBookingTests
{
    internal static AdminBookingRequest Request(DateOnly start, int nights = 4, bool standby = false,
        string email = "jane@example.com", decimal? price = null, PeriodType type = PeriodType.Custom, int adults = 3,
        int children = 1, string boatId = "pacific-blue") =>
        new(boatId, type, start, start.AddDays(nights), adults, children, null, null, email, "Jane Citizen", null, null,
            standby, null, price ?? (type == PeriodType.Custom ? 4000m : null), null);

    internal static AdminBookingUpdateRequest Update(BookingDetailDto b, BookingStatus? status = null, DateOnly? start = null,
        bool? standby = null, int? version = null) =>
        new(version ?? b.Version, b.BoatId, b.PeriodType, start ?? b.StartDate,
            (start ?? b.StartDate).AddDays(b.EndDate.DayNumber - b.StartDate.DayNumber), b.NumberOfAdults, b.NumberOfChildren,
            b.CheckInTime, b.CheckOutTime, b.Customer?.Email ?? b.CustomerDetails?.Email,
            b.Customer?.FullName ?? b.CustomerDetails?.FullName, b.Customer?.MobileNumber, b.Customer?.SecondaryPhone,
            standby ?? b.IsStandby, b.Comments, b.HirePrice, status ?? b.Status);

    [Fact]
    public async Task Staff_booking_is_active_immediately_and_sends_the_confirmation()
    {
        var app = await TestApp.CreateAsync(online: false);
        var booking = await app.Get<AdminBookingService>().CreateAsync(Request(app.MondayAfter(10)));
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(BookingCreatedBy.SystemUser, booking.CreatedBy);
        Assert.Equal((3, 1, 4), (booking.NumberOfAdults, booking.NumberOfChildren, booking.NumberOfGuests));
        Assert.Equal((new TimeOnly(11, 0), new TimeOnly(8, 0)), (booking.CheckInTime, booking.CheckOutTime));
        Assert.Equal(1000m, booking.DepositAmount);
        Assert.Null(booking.Customer!.SecondaryPhone);
        Assert.Single(app.Emails.Sent, m => m.To == "jane@example.com" && m.Subject.Contains("confirmed"));
    }

    [Fact]
    public async Task Staff_record_custom_times_and_a_secondary_phone()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10)) with
        {
            CheckInTime = new TimeOnly(9, 30), CheckOutTime = new TimeOnly(10, 0), Phone = "0412 345 678",
            SecondaryPhone = "+44 7911 123456",
        });
        Assert.Equal((new TimeOnly(9, 30), new TimeOnly(10, 0)), (booking.CheckInTime, booking.CheckOutTime));
        Assert.Equal(("+61412345678", "+447911123456"), (booking.Customer!.MobileNumber, booking.Customer.SecondaryPhone));

        var cleared = await admin.UpdateAsync(booking.Id, Update(booking) with { SecondaryPhone = null });
        Assert.Null(cleared.Customer!.SecondaryPhone);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            admin.UpdateAsync(booking.Id, Update(cleared) with { SecondaryPhone = "12" }));
        Assert.True(ex.Errors.ContainsKey("secondaryPhone"));
    }

    [Fact]
    public async Task Guest_and_bed_limits_are_only_warnings_for_staff()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var aboveBeds = await admin.CreateAsync(Request(app.MondayAfter(10), adults: 7, children: 2));
        Assert.Contains(aboveBeds.Warnings, w => w.Contains("only 8 beds"));
        var aboveMax = await admin.CreateAsync(Request(app.MondayAfter(20), adults: 10, children: 4, email: "b@example.com"));
        Assert.Contains(aboveMax.Warnings, w => w.Contains("maximum of 12"));
        var ok = await admin.CreateAsync(Request(app.MondayAfter(30), adults: 6, children: 2, email: "c@example.com"));
        Assert.Empty(ok.Warnings);

        var noAdult = await Assert.ThrowsAsync<RequestValidationException>(() =>
            admin.CreateAsync(Request(app.MondayAfter(40), adults: 0, children: 2)));
        Assert.True(noAdult.Errors.ContainsKey("numberOfAdults"));
    }

    [Fact]
    public async Task A_custom_time_that_clashes_with_a_same_day_changeover_is_a_warning()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var start = app.MondayAfter(10);
        var first = await admin.CreateAsync(Request(start) with { CheckOutTime = new TimeOnly(12, 0) });
        var second = await admin.CreateAsync(Request(start.AddDays(4), email: "b@example.com"));
        Assert.Contains(second.Warnings, w => w.Contains(first.Reference) && w.Contains("checks out at 12pm"));
        Assert.Contains((await admin.GetAsync(first.Id)).Warnings, w => w.Contains(second.Reference) && w.Contains("checks in at 11am"));
    }

    [Fact]
    public async Task Staff_can_book_blocked_periods_and_inside_the_lead_time()
    {
        var app = await TestApp.CreateAsync(online: false);
        var christmas = await app.Get<AdminBookingService>().CreateAsync(Request(new DateOnly(2026, 12, 22), 7));
        Assert.Equal(PaymentSchedule.Extended, christmas.PaymentSchedule);
        await app.Get<AdminBookingService>().CreateAsync(Request(app.Today.AddDays(1), 1));
    }

    [Fact]
    public async Task Rate_table_prices_are_used_when_no_price_is_given()
    {
        var app = await TestApp.CreateAsync(online: false);
        var mon = app.MondayAfter(10);
        var booking = await app.Get<AdminBookingService>().CreateAsync(Request(mon, type: PeriodType.Week, nights: 7));
        Assert.Equal(5850m, booking.HirePrice);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            app.Get<AdminBookingService>().CreateAsync(Request(mon.AddDays(14), price: null) with { HirePrice = null }));

        var quote = await app.Get<AdminBookingService>().QuoteAsync("pacific-blue", PeriodType.Midweek, new DateOnly(2027, 5, 3));
        Assert.Equal(3570m, quote.HirePrice);
        Assert.Equal("Off Peak", quote.SeasonName);
        Assert.Equal(new DateOnly(2027, 5, 7), quote.EndDate);
    }

    [Fact]
    public async Task Overlap_is_rejected_with_the_conflicting_reference()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = app.MondayAfter(10);
        var first = await app.Get<AdminBookingService>().CreateAsync(Request(start));
        var ex = await Assert.ThrowsAsync<AvailabilityConflictException>(() =>
            app.Get<AdminBookingService>().CreateAsync(Request(start.AddDays(2), standby: true)));
        Assert.Contains(first.Reference, ex.ConflictingReferences);

        // Same-day changeover is fine.
        await app.Get<AdminBookingService>().CreateAsync(Request(start.AddDays(4), 3, email: "bob@example.com"));
    }

    [Fact]
    public async Task Unavailability_blocks_staff_bookings_and_vice_versa()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = app.MondayAfter(10);
        var fleet = app.Get<FleetService>();
        await fleet.CreateUnavailabilityAsync("pacific-blue", new UnavailabilityRequest(0, start.AddDays(1), start.AddDays(1), "Slipping"));
        await Assert.ThrowsAsync<AvailabilityConflictException>(() => app.Get<AdminBookingService>().CreateAsync(Request(start)));

        var booking = await app.Get<AdminBookingService>().CreateAsync(Request(start.AddDays(7)));
        var ex = await Assert.ThrowsAsync<AvailabilityConflictException>(() =>
            fleet.CreateUnavailabilityAsync("pacific-blue", new UnavailabilityRequest(0, start.AddDays(9), start.AddDays(20), null)));
        Assert.Contains(booking.Reference, ex.Message);
    }

    [Fact]
    public async Task Standbys_send_no_email_and_are_superseded_by_a_new_booking()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = app.MondayAfter(10);
        var admin = app.Get<AdminBookingService>();
        var standby1 = await admin.CreateAsync(Request(start, standby: true, email: "a@example.com"));
        var standby2 = await admin.CreateAsync(Request(start.AddDays(1), standby: true, email: "b@example.com"));
        Assert.Empty(app.Emails.Sent);

        await admin.CreateAsync(Request(start.AddDays(3), 2, email: "c@example.com"));

        Assert.Equal(BookingStatus.Superseded, (await admin.GetAsync(standby1.Id)).Status);
        Assert.Equal(BookingStatus.Superseded, (await admin.GetAsync(standby2.Id)).Status);
        Assert.Equal(2, app.Emails.Sent.Count(m => m.Subject.Contains("Stand-by booking") && m.To == "staff@example.test"));
        Assert.Single(app.Emails.Sent, m => m.To == "c@example.com");
    }

    [Fact]
    public async Task Version_mismatch_is_a_concurrency_conflict()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10)));
        await admin.UpdateAsync(booking.Id, Update(booking) with { Comments = "first edit" });
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            admin.UpdateAsync(booking.Id, Update(booking) with { Comments = "stale edit" }));
        Assert.Equal("This record was changed elsewhere — reload and retry.", ex.Message);
    }

    [Fact]
    public async Task Cancel_frees_the_dates_and_reinstating_checks_availability()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var start = app.MondayAfter(10);
        var booking = await admin.CreateAsync(Request(start));
        var cancelled = await admin.UpdateAsync(booking.Id, Update(booking, BookingStatus.Cancelled));
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        app.Emails.Sent.Clear();

        await admin.CreateAsync(Request(start, email: "bob@example.com"));
        Assert.DoesNotContain(app.Emails.Sent, m => m.To == "jane@example.com"); // no cancellation email (decision 10)

        await Assert.ThrowsAsync<AvailabilityConflictException>(() => admin.UpdateAsync(booking.Id, Update(cancelled, BookingStatus.Active)));
        var moved = await admin.UpdateAsync(booking.Id, Update(cancelled, BookingStatus.Active, start.AddDays(14)));
        Assert.Equal(BookingStatus.Active, moved.Status);
        Assert.DoesNotContain(app.Emails.Sent, m => m.To == "jane@example.com"); // confirmation was already sent once
    }

    [Fact]
    public async Task Converting_a_standby_to_a_real_booking_sends_the_confirmation_once()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var standby = await admin.CreateAsync(Request(app.MondayAfter(10), standby: true));
        Assert.Empty(app.Emails.Sent);
        await admin.UpdateAsync(standby.Id, Update(standby, standby: false));
        Assert.Single(app.Emails.Sent);
    }

    [Fact]
    public async Task Superseded_bookings_only_take_comments_and_customer_details()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10), standby: true));
        await admin.CreateAsync(Request(app.MondayAfter(10), email: "b@example.com"));
        var superseded = await admin.GetAsync(booking.Id);
        await Assert.ThrowsAsync<RequestValidationException>(() => admin.UpdateAsync(booking.Id, Update(superseded, BookingStatus.Active)));
        var commented = await admin.UpdateAsync(booking.Id, Update(superseded) with { Comments = "Called customer" });
        Assert.Equal("Called customer", commented.Comments);
    }

    [Fact]
    public async Task Add_on_lines_count_once_priced_are_declined_by_removing_and_payments_update_the_status()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10), price: 4000m));
        booking = await admin.AddAddonAsync(booking.Id, new AddAddonLineRequest(booking.Version, "welcome-hamper", 1));
        var line = Assert.Single(booking.AddonLines);
        Assert.Null(line.UnitPrice);
        Assert.Equal(4000m, booking.TotalPrice);
        booking = await admin.UpdateAddonAsync(booking.Id, line.Id, new UpdateAddonLineRequest(booking.Version, 1, 150m));
        Assert.Equal(4150m, booking.TotalPrice);

        booking = await admin.AddAddonAsync(booking.Id, new AddAddonLineRequest(booking.Version, "skipper", 1));
        var skipper = booking.AddonLines.Single(l => l.AddonId == "skipper");
        booking = await admin.RemoveAddonAsync(booking.Id, skipper.Id, booking.Version);
        Assert.Single(booking.AddonLines);

        app.Emails.Sent.Clear();
        booking = await admin.AddManualPaymentAsync(booking.Id, new ManualPaymentRequest(booking.Version, 500m, app.Today, "EFT"));
        Assert.Equal(PaymentStatus.PartPaid, booking.PaymentStatus); // below the deposit
        booking = await admin.AddManualPaymentAsync(booking.Id, new ManualPaymentRequest(booking.Version, 500m, app.Today, "EFT"));
        Assert.Equal(PaymentStatus.DepositPaid, booking.PaymentStatus); // exactly the deposit
        Assert.All(app.Emails.Sent, m => Assert.Contains("Payment received", m.Subject));
        Assert.Equal(3150m, booking.AmountDueNow);

        app.Emails.Sent.Clear();
        var link = await admin.SendPaymentLinkAsync(booking.Id);
        Assert.Equal(3150m, link.AmountDue);
        await admin.SendPaymentLinkAsync(booking.Id); // manual sends are not deduplicated
        Assert.Equal(2, app.Emails.Sent.Count);

        booking = await admin.AddManualPaymentAsync(booking.Id, new ManualPaymentRequest(booking.Version, 3150m, app.Today, null));
        Assert.Equal(PaymentStatus.FullyPaid, booking.PaymentStatus);
        await Assert.ThrowsAsync<RequestValidationException>(() => admin.SendPaymentLinkAsync(booking.Id));
    }

    [Fact]
    public async Task Add_ons_on_long_weekend_and_custom_bookings_have_no_price_until_staff_enter_one()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var friday = app.MondayAfter(10).AddDays(4);
        var booking = await admin.CreateAsync(Request(friday, type: PeriodType.LongWeekend));
        booking = await admin.AddAddonAsync(booking.Id, new AddAddonLineRequest(booking.Version, "outboard-motor", 1));
        Assert.Null(Assert.Single(booking.AddonLines).UnitPrice);

        var midweek = await admin.CreateAsync(Request(app.MondayAfter(30), type: PeriodType.Midweek, email: "b@example.com"));
        midweek = await admin.AddAddonAsync(midweek.Id, new AddAddonLineRequest(midweek.Version, "outboard-motor", 1));
        Assert.Equal(95m, Assert.Single(midweek.AddonLines).UnitPrice);
    }

    [Fact]
    public async Task Car_parking_over_capacity_is_a_warning_for_staff()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var start = app.MondayAfter(10);
        var first = await admin.CreateAsync(Request(start) with
        {
            Addons = [new AdminAddonLineRequest("car-parking", 8, null)],
        });
        var line = Assert.Single(first.AddonLines);
        Assert.Equal((11, 11), (line.Capacity, line.Remaining)); // excluding this booking
        Assert.Empty(first.Warnings);

        var capacity = await app.Get<AddonCapacityQueries>().RemainingAsync("car-parking", start, start.AddDays(4));
        Assert.Equal(3, capacity.Remaining);
        var excluding = await app.Get<AddonCapacityQueries>().RemainingAsync("car-parking", start, start.AddDays(4), first.Id);
        Assert.Equal(11, excluding.Remaining);

        var detail = await admin.AddAddonAsync(first.Id, new AddAddonLineRequest(first.Version, "car-parking", 4));
        Assert.Contains(detail.Warnings, w => w.StartsWith("This exceeds car parking capacity on "));
    }

    [Fact]
    public async Task Rebook_prefills_next_years_dates_current_prices_guests_times_customer_and_add_ons()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var original = await admin.CreateAsync(Request(new DateOnly(2026, 11, 16), type: PeriodType.Midweek, adults: 5,
            children: 3) with
        {
            HirePrice = 3999m, CheckInTime = new TimeOnly(12, 0), Phone = "0412 345 678", SecondaryPhone = "(02) 4444 4444",
            Comments = "Not copied", Addons = [new AdminAddonLineRequest("outboard-motor", 1, 80m), new AdminAddonLineRequest("ice", 3, null)],
        });

        var rebook = await admin.GetRebookAsync(original.Id);

        Assert.Equal(original.Reference, rebook.OriginalReference);
        Assert.Equal(new DateOnly(2027, 11, 16), rebook.StartDate); // a Tuesday in 2027
        Assert.Equal(new DateOnly(2027, 11, 20), rebook.EndDate);
        Assert.Equal(PeriodType.Custom, rebook.PeriodType);
        Assert.Equal(3999m, rebook.HirePrice); // Custom keeps the original price
        Assert.Equal((5, 3), (rebook.NumberOfAdults, rebook.NumberOfChildren));
        Assert.Equal(new TimeOnly(12, 0), rebook.CheckInTime);
        Assert.Equal(("jane@example.com", "Jane Citizen", "+61412345678", "+61244444444"),
            (rebook.Email, rebook.FullName, rebook.Phone, rebook.SecondaryPhone));
        Assert.Equal(["ice", "outboard-motor"], rebook.Addons.Select(a => a.AddonId));
        Assert.Equal(80m, rebook.Addons.Single(a => a.AddonId == "outboard-motor").UnitPrice); // Custom: no current price

        var created = await admin.CreateAsync(new AdminBookingRequest(rebook.BoatId, rebook.PeriodType, rebook.StartDate,
            rebook.EndDate, rebook.NumberOfAdults, rebook.NumberOfChildren, rebook.CheckInTime, rebook.CheckOutTime, rebook.Email,
            rebook.FullName, rebook.Phone, rebook.SecondaryPhone, false, null, rebook.HirePrice,
            rebook.Addons.Select(a => new AdminAddonLineRequest(a.AddonId, a.Quantity, a.UnitPrice)).ToList()));
        Assert.Equal(2, created.AddonLines.Count);
        Assert.Null(created.Comments);
    }

    [Fact]
    public async Task Rebook_keeps_a_period_type_that_still_fits_and_uses_current_rates()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        // Fri 06/11/2026 → Mon 09/11/2026; 06/11/2027 is a Saturday, so it becomes Custom; LongWeekend is kept.
        var longWeekend = await admin.CreateAsync(Request(new DateOnly(2026, 11, 6), type: PeriodType.LongWeekend) with
        {
            HirePrice = 100m,
            Addons = [new AdminAddonLineRequest("outboard-motor", 1, 80m)],
        });
        var rebook = await admin.GetRebookAsync(longWeekend.Id);
        Assert.Equal(PeriodType.LongWeekend, rebook.PeriodType);
        Assert.Equal(4620m, rebook.HirePrice); // current Normal long weekend rate
        Assert.Equal(80m, Assert.Single(rebook.Addons).UnitPrice); // no long weekend add-on prices

        var midweek = await admin.CreateAsync(Request(new DateOnly(2025, 11, 17), type: PeriodType.Midweek, email: "b@example.com")
            with { HirePrice = 100m });
        var next = await admin.GetRebookAsync(midweek.Id); // 17/11/2026 is a Tuesday → Custom
        Assert.Equal(PeriodType.Custom, next.PeriodType);
        Assert.Equal(new DateOnly(2026, 11, 17), next.StartDate);
    }

    [Fact]
    public async Task Customers_list_all_their_bookings_newest_first()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var older = await admin.CreateAsync(Request(app.MondayAfter(10)));
        var newer = await admin.CreateAsync(Request(app.MondayAfter(40)));
        await admin.UpdateAsync(older.Id, Update(older, BookingStatus.Cancelled));

        var bookings = await app.Get<CustomerQueries>().ListBookingsAsync(newer.Customer!.Id);
        Assert.Equal([newer.Reference, older.Reference], bookings.Select(b => b.Reference));
        Assert.Equal(BookingStatus.Cancelled, bookings[1].Status);
        Assert.Equal("Pacific Blue", bookings[0].BoatName);
    }

    [Fact]
    public async Task Recalculate_price_reprices_from_rates_but_changing_dates_does_not()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var mon = app.MondayAfter(10);
        var booking = await admin.CreateAsync(Request(mon, type: PeriodType.Midweek) with { HirePrice = 3333m });
        Assert.Equal(3333m, booking.HirePrice);
        var moved = await admin.UpdateAsync(booking.Id, Update(booking, start: mon.AddDays(7)));
        Assert.Equal(3333m, moved.HirePrice);
        var repriced = await admin.RecalculatePriceAsync(booking.Id, new VersionRequest(moved.Version));
        Assert.Equal(4060m, repriced.HirePrice);
    }

    [Fact]
    public async Task Search_finds_bookings_by_name_email_and_reference()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10)));
        Assert.Single(await admin.SearchAsync("citizen"));
        Assert.Single(await admin.SearchAsync("JANE@example"));
        Assert.Single(await admin.SearchAsync(booking.Reference.ToLowerInvariant()));
        Assert.Empty(await admin.SearchAsync("nobody"));
    }

    [Fact]
    public async Task Timeline_hides_inactive_bookings_unless_asked()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var start = app.MondayAfter(10);
        var booking = await admin.CreateAsync(Request(start));
        await admin.UpdateAsync(booking.Id, Update(booking, BookingStatus.Cancelled));
        var from = new DateOnly(start.Year, start.Month, 1);
        Assert.Empty((await admin.GetTimelineAsync(from, from.AddMonths(1), false)).Bookings);
        var all = await admin.GetTimelineAsync(from, from.AddMonths(1), true);
        Assert.Equal("Jane Citizen", Assert.Single(all.Bookings).CustomerName);
        Assert.Equal(14, all.Boats.Count);
    }

    [Fact]
    public async Task Every_seeded_boat_is_complete_and_only_the_listed_boats_have_their_own_deposit()
    {
        var app = await TestApp.CreateAsync(online: false);
        var seasons = await app.Get<ISeasonRepository>().ListAsync();
        var boats = await app.Get<IBoatRepository>().ListAsync();

        Assert.All(boats, b => Assert.Empty(b.MissingActivationData(seasons)));
        Assert.Equal(
            new Dictionary<string, decimal?> { ["pacific-blue"] = 1000m, ["ocean-spirit"] = 1000m, ["island-dream"] = 1000m, ["runaway-bear"] = 250m },
            boats.Where(b => b.DepositAmount is not null).ToDictionary(b => b.Slug, b => b.DepositAmount));
    }

    [Fact]
    public async Task Boats_are_listed_largest_first_then_by_name()
    {
        var app = await TestApp.CreateAsync(online: false);
        var from = app.Today;
        var timeline = await app.Get<AdminBookingService>().GetTimelineAsync(from, from.AddMonths(1), false);
        var expected = new[]
        {
            "Blue Bayou", "Gypsea Belle", "Image Anne", "Island Dream", "Kalinda", "Ocean Spirit", "Pacific Blue",
            "Paradise II", "Misty Blue", "JFS Rhyanna", "Marie Claire", "Wanderer", "Wavebreak", "Runaway Bear",
        };
        Assert.Equal(expected, timeline.Boats.Select(b => b.Name));
        Assert.Equal(expected, (await app.Get<FleetService>().ListAsync()).Select(b => b.Name));
    }

    [Fact]
    public async Task Online_holds_without_a_customer_show_and_search_by_the_details_entered()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = app.MondayAfter(40);
        var id = BookingId.New();
        var hold = Booking.CreateOnlineHold(BookingReference.Create(2026, 7000),
            new BookingCustomerDetails("Olivia Online", "olivia@example.com", "0412 345 678"), BookingDraftId.New(),
            TestApp.PacificBlue, PeriodType.Midweek, new Stay(start, start.AddDays(4)), 2, 0, 4060m, 1000m,
            PaymentSchedule.Standard, [], app.Clock.UtcNow.AddMinutes(40), null, app.Clock.UtcNow,
            app.Get<PaymentLinkTokens>().HashFor(id), app.Clock.UtcNow, id);
        await app.Get<IBookingRepository>().SaveAsync(hold);

        var admin = app.Get<AdminBookingService>();
        var found = Assert.Single(await admin.SearchAsync("olivia"));
        Assert.Equal(("Olivia Online", "olivia@example.com"), (found.CustomerName, found.CustomerEmail));
        var detail = await admin.GetAsync(id.Value);
        Assert.Null(detail.Customer);
        Assert.Equal("+61412345678", detail.CustomerDetails!.MobileNumber);
        var from = new DateOnly(start.Year, start.Month, 1);
        Assert.Equal("Olivia Online", Assert.Single((await admin.GetTimelineAsync(from, from.AddMonths(1), false)).Bookings).CustomerName);

        // Editing an expired hold's comments never creates a customer.
        app.Clock.Advance(TimeSpan.FromHours(1));
        await app.Get<BergerHarbour.Application.Notifications.NotificationJob>().RunAsync();
        var expired = await admin.GetAsync(id.Value);
        await admin.UpdateAsync(id.Value, Update(expired) with { Comments = "Called her" });
        Assert.Empty(app.Store.Customers.All());
    }

    [Fact]
    public async Task Inactive_boats_cannot_be_booked_by_staff()
    {
        var app = await TestApp.CreateAsync(online: false);
        var boats = app.Get<IBoatRepository>();
        var kalinda = (await boats.GetBySlugAsync("kalinda"))!;
        kalinda.Deactivate(app.Clock.UtcNow);
        await boats.SaveAsync(kalinda);
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            app.Get<AdminBookingService>().CreateAsync(Request(app.MondayAfter(10)) with { BoatId = "kalinda" }));
        Assert.True(ex.Errors.ContainsKey("boatId"));
    }
}
