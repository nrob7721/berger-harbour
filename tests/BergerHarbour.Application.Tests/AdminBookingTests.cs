using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class AdminBookingTests
{
    private static AdminBookingRequest Request(DateOnly start, int nights = 4, bool standby = false,
        string email = "jane@example.com", decimal? price = null, PeriodType type = PeriodType.Custom) =>
        new("pacific-blue", type, start, start.AddDays(nights), 4, email, "Jane Citizen", null, standby, null,
            price ?? (type == PeriodType.Custom ? 4000m : null));

    private static AdminBookingUpdateRequest Update(BookingDetailDto b, BookingStatus? status = null, DateOnly? start = null,
        bool? standby = null, int? version = null) =>
        new(version ?? b.Version, b.BoatId, b.PeriodType, start ?? b.StartDate, (start ?? b.StartDate).AddDays(b.EndDate.DayNumber - b.StartDate.DayNumber),
            b.NumberOfGuests, b.Customer.Email, b.Customer.FullName, b.Customer.MobileNumber, standby ?? b.IsStandby, b.Comments,
            b.HirePrice, status ?? b.Status);

    [Fact]
    public async Task Staff_booking_is_active_immediately_and_sends_the_confirmation()
    {
        var app = await TestApp.CreateAsync(online: false);
        var booking = await app.Get<AdminBookingService>().CreateAsync(Request(app.MondayAfter(10)));
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(BookingCreatedBy.SystemUser, booking.CreatedBy);
        Assert.Single(app.Emails.Sent, m => m.To == "jane@example.com" && m.Subject.Contains("confirmed"));
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
    public async Task Expired_holds_cannot_be_edited()
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
    public async Task Confirmed_addons_raise_the_total_and_payments_update_the_status()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(Request(app.MondayAfter(10), price: 4000m));
        booking = await admin.AddAddonAsync(booking.Id, new AddAddonLineRequest(booking.Version, "welcome-hamper", 1));
        var line = Assert.Single(booking.AddonLines);
        Assert.Null(line.UnitPrice);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            admin.UpdateAddonAsync(booking.Id, line.Id, new UpdateAddonLineRequest(booking.Version, 1, null, AddonLineStatus.Confirmed)));
        booking = await admin.UpdateAddonAsync(booking.Id, line.Id, new UpdateAddonLineRequest(booking.Version, 1, 150m, AddonLineStatus.Confirmed));
        Assert.Equal(4150m, booking.TotalPrice);

        app.Emails.Sent.Clear();
        booking = await admin.AddManualPaymentAsync(booking.Id, new ManualPaymentRequest(booking.Version, 1000m, app.Today, "EFT"));
        Assert.Equal(PaymentStatus.DepositPaid, booking.PaymentStatus);
        Assert.Contains("Payment received", Assert.Single(app.Emails.Sent).Subject);
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
    public async Task Inactive_boats_cannot_be_booked_by_staff()
    {
        var app = await TestApp.CreateAsync(online: false);
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            app.Get<AdminBookingService>().CreateAsync(Request(app.MondayAfter(10)) with { BoatId = "kalinda" }));
        Assert.True(ex.Errors.ContainsKey("boatId"));
    }
}
