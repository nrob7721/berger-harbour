using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Settings;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Harness;

namespace BergerHarbour.Application.Tests;

public class StaffBookingTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Staff_booking_is_active_with_reference_customer_and_confirmation()
    {
        var r = await _h.Admin.CreateAsync(_h.StaffRequest("2027-01-15", "2027-01-18", customerComments: "Repeat guest"));
        Assert.Equal(BookingStatus.Active, r.Booking.Status);
        Assert.Equal("SMITH-2701150", r.Booking.Reference);
        Assert.Equal("Repeat guest", r.Booking.Customer!.Comments);
        Assert.Equal(PaymentScheduleType.Standard, r.Booking.PaymentScheduleType); // after Christmas / New Year (last night 05/01)
        Assert.Single(_h.Emails.WithSubject("is confirmed"));
        var second = await _h.Admin.CreateAsync(_h.StaffRequest("2027-01-15", "2027-01-18", boatSlug: "pacific-blue", email: "andy@example.com") with { StartDate = D("2027-01-18"), EndDate = D("2027-01-20") });
        Assert.Equal("SMITH-2701180", second.Booking.Reference);
    }

    [Fact]
    public async Task Standby_bookings_send_no_email_and_are_superseded_with_a_staff_alert()
    {
        var standby = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", standby: true, email: "s@example.com"));
        Assert.Empty(_h.Emails.Sent);
        var standby2 = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-09", "2027-03-11", standby: true, email: "t@example.com")); // stand-bys may overlap
        await _h.BookOnlineAsync();
        Assert.Equal(BookingStatus.Superseded, _h.Db.Bookings.Get(standby.Booking.Id)!.Status);
        Assert.Equal(BookingStatus.Superseded, _h.Db.Bookings.Get(standby2.Booking.Id)!.Status);
        Assert.Equal(2, _h.Emails.WithSubject("Stand-by booking superseded").Count);
    }

    [Fact]
    public async Task Inactive_boats_cannot_be_booked_by_staff()
    {
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", boatSlug: "kalinda")));
        Assert.Contains("boatId", e.Errors.Keys);
    }

    [Fact]
    public async Task Addon_lines_are_added_updated_and_deleted_with_the_booking()
    {
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", type: PeriodType.Midweek,
            lines: [new(null, "ice", 2, 5), new(null, "welcome-hamper", 1, null)]));
        var ice = created.Booking.AddonLines.Single(l => l.AddonId == "ice");
        var updated = await _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with
        {
            AddonLines = [new(ice.Id, "ice", 4, 6), new(null, "outboard-motor", 1, 95)],
        }));
        Assert.Equal(2, updated.Booking.AddonLines.Count);
        Assert.Equal(ice.Id, updated.Booking.AddonLines.Single(l => l.AddonId == "ice").Id);
        Assert.Equal(24m, updated.Booking.AddonLines.Single(l => l.AddonId == "ice").LineTotal);
        Assert.DoesNotContain(updated.Booking.AddonLines, l => l.AddonId == "welcome-hamper");
        Assert.Equal(4000m + 24 + 95, updated.Booking.TotalPrice);
    }

    [Fact]
    public async Task Quote_prefills_prices_and_leaves_staff_entered_ones_null()
    {
        var boatId = _h.Boat("pacific-blue").Id.Value;
        var midweek = await _h.Admin.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.Midweek, D("2027-03-08"), ["outboard-motor", "welcome-hamper"]));
        Assert.Equal(4060m, midweek.HirePrice);
        Assert.Equal(95m, midweek.AddonPrices.Single(p => p.AddonId == "outboard-motor").UnitPrice);
        Assert.Null(midweek.AddonPrices.Single(p => p.AddonId == "welcome-hamper").UnitPrice);
        var longWeekend = await _h.Admin.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.LongWeekend, D("2027-03-05"), ["outboard-motor"]));
        Assert.Equal(4620m, longWeekend.HirePrice);
        Assert.Null(longWeekend.AddonPrices.Single().UnitPrice);
        var custom = await _h.Admin.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.Custom, D("2027-03-05"), ["outboard-motor"]));
        Assert.Null(custom.HirePrice);
        Assert.Null(custom.AddonPrices.Single().UnitPrice);
    }

    [Fact]
    public async Task Overlaps_name_the_conflicting_reference_and_versions_are_checked()
    {
        var first = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var e = await Assert.ThrowsAsync<ConflictException>(() => _h.Admin.CreateAsync(_h.StaffRequest("2027-03-11", "2027-03-14", email: "x@example.com")));
        Assert.Contains("SMITH-2703080", e.ConflictingReferences);
        await _h.Admin.UpdateAsync(first.Booking.Id, _h.UpdateRequest(first.Booking, r => r with { BookingComments = "one" }));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _h.Admin.UpdateAsync(first.Booking.Id, _h.UpdateRequest(first.Booking, r => r with { BookingComments = "stale" })));
    }

    [Fact]
    public async Task Cancel_and_reactivate_checks_availability()
    {
        var first = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var cancelled = await _h.Admin.UpdateAsync(first.Booking.Id, _h.UpdateRequest(first.Booking, r => r with { Status = BookingStatus.Cancelled }));
        Assert.Equal(BookingStatus.Cancelled, cancelled.Booking.Status);
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _h.Admin.UpdateAsync(first.Booking.Id, _h.UpdateRequest(cancelled.Booking, r => r with { Status = BookingStatus.Active })));
    }

    [Fact]
    public async Task Customer_comments_are_saved_from_create_update_and_the_customers_page()
    {
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", customerComments: "First"));
        var updated = await _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with { CustomerComments = "Second", SecondaryMobile = "0499 888 777" }));
        Assert.Equal("Second", updated.Booking.Customer!.Comments);
        Assert.Equal("+61499888777", updated.Booking.Customer.SecondaryMobile);
        var customers = _h.Get<CustomerAdminService>();
        var c = await customers.UpdateCommentsAsync(updated.Booking.Customer.Id, new UpdateCustomerCommentsRequest("Third", updated.Booking.Customer.Version));
        Assert.Equal("Third", c.Comments);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            customers.UpdateCommentsAsync(c.Id, new UpdateCustomerCommentsRequest("Stale", updated.Booking.Customer.Version)));
    }

    [Fact]
    public async Task Address_is_saved_from_staff_create_and_update_and_returned_by_the_lookup()
    {
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", address: "  1 River Rd\nMannum SA 5238  "));
        Assert.Equal("1 River Rd\nMannum SA 5238", created.Booking.Customer!.Address);
        var updated = await _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with { Address = "2 Wharf St" }));
        Assert.Equal("2 Wharf St", updated.Booking.Customer!.Address);
        var lookup = await _h.Get<CustomerAdminService>().GetByEmailAsync("john@example.com");
        Assert.Equal("2 Wharf St", lookup.Customer.Address);
        var cleared = await _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(updated.Booking, r => r with { Address = "  " }));
        Assert.Null(cleared.Booking.Customer!.Address);
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(cleared.Booking, r => r with { Address = new string('a', 501) })));
        Assert.Contains("address", e.Errors.Keys);
    }

    [Theory]
    [InlineData("jane@gmail")]
    [InlineData("jane@gmail.c0m")]
    public async Task Staff_create_and_update_reject_an_invalid_email(string email)
    {
        var create = await Assert.ThrowsAsync<DomainValidationException>(() => _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", email: email)));
        Assert.Equal([EmailAddress.InvalidMessage], create.Errors["email"]);
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var update = await Assert.ThrowsAsync<DomainValidationException>(() =>
            _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with { Email = email })));
        Assert.Equal([EmailAddress.InvalidMessage], update.Errors["email"]);
    }

    [Fact]
    public async Task Staff_update_rejects_an_invalid_mobile()
    {
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with { Mobile = "02 9876 5432", SecondaryMobile = "12345" })));
        Assert.Equal([MobileNumber.InvalidMessage], e.Errors["mobile"]);
        Assert.Equal([MobileNumber.InvalidMessage], e.Errors["secondaryMobile"]);
    }

    [Fact]
    public async Task Changing_the_email_moves_the_booking_to_that_customer()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-05-03", "2027-05-07", email: "other@example.com", lastName: "Other"));
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var moved = await _h.Admin.UpdateAsync(created.Booking.Id, _h.UpdateRequest(created.Booking, r => r with { Email = "OTHER@example.com", LastName = "Other" }));
        Assert.Equal("other@example.com", moved.Booking.Customer!.Email);
        Assert.Equal(2, _h.Db.Customers.All.Count);
        Assert.Equal("SMITH-2703080", moved.Booking.Reference); // never changes
    }

    [Fact]
    public async Task Customer_lookup_and_frequent_floater_badge()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-05-03", "2027-05-07"));
        var lookup = await _h.Get<CustomerAdminService>().GetByEmailAsync("JOHN@example.com");
        Assert.True(lookup.FrequentFloater.IsFrequentFloater);
        var second = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        Assert.Equal(new FrequentFloaterDto(true, 1), second.Booking.FrequentFloater);
    }

    [Fact]
    public async Task Manual_payment_sends_a_receipt_and_payment_link_requires_an_amount_owing()
    {
        var created = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", hire: 1000));
        await _h.Admin.SendPaymentLinkAsync(created.Booking.Id);
        Assert.Single(_h.Emails.WithSubject("Payment due"));
        var paid = await _h.Admin.AddManualPaymentAsync(created.Booking.Id, new ManualPaymentRequest(1000, D("2026-11-09"), "Bank transfer", created.Booking.Version));
        Assert.Equal(PaymentStatus.FullyPaid, paid.PaymentStatus);
        Assert.Single(_h.Emails.WithSubject("Payment received"));
        await Assert.ThrowsAsync<DomainValidationException>(() => _h.Admin.SendPaymentLinkAsync(created.Booking.Id));
    }

    [Fact]
    public async Task Search_matches_names_emails_references_and_online_snapshots()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        var draft = await _h.CreateDraftAsync(start: "2027-04-05", email: "zed@example.com", lastName: "Zed");
        await _h.Online.CreateBookingAsync(_h.BookingRequest(draft, start: "2027-04-05", email: "zed@example.com", lastName: "Zed"));
        Assert.Single(await _h.Admin.SearchAsync("smith-2703"));
        Assert.Single(await _h.Admin.SearchAsync("john@"));
        Assert.Single(await _h.Admin.SearchAsync("Jane Zed"));
    }
}

public class NotificationJobTests
{
    private readonly Harness _h;

    public NotificationJobTests()
    {
        _h = new Harness();
        // Shorter offsets so the scenarios stay readable.
        _h.NotificationOptions.BalanceDueBeforeHire = TimeSpan.FromDays(30);
        _h.NotificationOptions.PaymentReminderBeforeDue = TimeSpan.FromDays(7);
    }

    private NotificationJob Job => _h.Get<NotificationJob>();

    private DateTimeOffset CheckIn(string date) => Domain.Shared.SydneyTime.CheckInInstant(D(date));

    [Fact]
    public async Task Sends_reminder_then_due_then_overdue_once_each()
    {
        var b = await _h.BookOnlineAsync(start: "2027-03-08");
        _h.Emails.Sent.Clear();
        var due = CheckIn("2027-03-08").AddDays(-30);

        _h.Clock.UtcNow = due.AddDays(-7).AddMinutes(-1);
        await Job.RunAsync();
        Assert.Empty(_h.Emails.Sent);

        _h.Clock.UtcNow = due.AddDays(-7);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Single(_h.Emails.WithSubject("Payment due"));

        _h.Clock.UtcNow = due;
        await Job.RunAsync();
        Assert.Equal(2, _h.Emails.WithSubject("Payment due").Count);
        Assert.Empty(_h.Emails.WithSubject("Overdue"));

        _h.Clock.UtcNow = due.AddHours(1);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Single(_h.Emails.WithSubject("Overdue"));
        Assert.Contains(NotificationKeys.Overdue(MilestoneKind.Balance), _h.Db.Bookings.Get(b.Id)!.SentNotifications.Select(n => n.Key));
    }

    [Fact]
    public async Task No_payment_emails_once_fully_paid()
    {
        var b = await _h.BookOnlineAsync(start: "2027-03-08");
        await _h.Admin.AddManualPaymentAsync(b.Id, new ManualPaymentRequest(b.AmountOwing, D("2026-11-10"), null, b.Version));
        _h.Emails.Sent.Clear();
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-20);
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Payment due"));
        Assert.Empty(_h.Emails.WithSubject("Overdue"));
    }

    [Fact]
    public async Task Booking_made_35_days_before_gets_no_reminder_but_gets_the_due_email()
    {
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-35);
        var b = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        _h.Emails.Sent.Clear();
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-31);
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Payment due"));
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-30);
        await Job.RunAsync();
        Assert.Single(_h.Emails.WithSubject("Payment due"));
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Booking_made_10_days_before_gets_no_payment_or_overdue_emails_and_its_confirmation_has_the_link()
    {
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-10);
        var b = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", email: "late@example.com"));
        var confirmation = _h.Emails.WithSubject("is confirmed").Single();
        Assert.Contains("is now due", confirmation.HtmlBody);
        Assert.Contains($"{BaseUrl}/pay/?token=", confirmation.HtmlBody);
        _h.Emails.Sent.Clear();
        for (var day = 0; day < 9; day++)
        {
            _h.Clock.Advance(TimeSpan.FromDays(1));
            await Job.RunAsync();
        }
        Assert.Empty(_h.Emails.WithSubject("Payment due"));
        Assert.Empty(_h.Emails.WithSubject("Overdue"));
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Pre_hire_instructions_are_sent_seven_days_before_or_at_the_next_run_when_booked_inside_the_window()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", hire: 1000));
        _h.Emails.Sent.Clear();
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-7).AddMinutes(-1);
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Getting ready"));
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-7);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Single(_h.Emails.WithSubject("Getting ready"));

        _h.Clock.UtcNow = CheckIn("2027-04-05").AddDays(-3);
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-04-05", "2027-04-09", email: "inside@example.com"));
        await Job.RunAsync();
        Assert.Equal(2, _h.Emails.WithSubject("Getting ready").Count);
    }

    [Fact]
    public async Task Extended_schedule_reminds_for_both_instalments()
    {
        var b = await _h.Admin.CreateAsync(_h.StaffRequest("2027-12-27", "2028-01-03", hire: 8000));
        Assert.Equal(PaymentScheduleType.Standard, b.Booking.PaymentScheduleType); // the seeded Christmas period is 2026–27
        var christmas = Domain.Settings.BlockedPeriod.Create("Christmas 2027", D("2027-12-20"), D("2028-01-05"), true, _h.Clock.UtcNow);
        await _h.Db.SaveAsync(christmas);
        var ext = await _h.Admin.CreateAsync(_h.StaffRequest("2027-12-20", "2027-12-27", hire: 8000, email: "x@example.com"));
        Assert.Equal(PaymentScheduleType.Extended, ext.Booking.PaymentScheduleType);
        _h.Emails.Sent.Clear();
        _h.Clock.UtcNow = CheckIn("2027-12-20").AddDays(-120);
        await Job.RunAsync();
        Assert.Contains(_h.Emails.WithSubject("Payment due"), m => m.HtmlBody.Contains("$4,000.00")); // 50% of 8000, nothing paid yet
        _h.Clock.UtcNow = CheckIn("2027-12-20").AddDays(-90);
        await Job.RunAsync();
        Assert.Contains(NotificationKeys.PaymentDue(MilestoneKind.Final), _h.Db.Bookings.Get(ext.Booking.Id)!.SentNotifications.Select(n => n.Key));
    }

    [Fact]
    public async Task Stand_by_and_cancelled_bookings_get_no_emails()
    {
        var s = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", standby: true));
        var c = await _h.Admin.CreateAsync(_h.StaffRequest("2027-04-05", "2027-04-09", email: "c@example.com"));
        await _h.Admin.UpdateAsync(c.Booking.Id, _h.UpdateRequest(c.Booking, r => r with { Status = BookingStatus.Cancelled }));
        _h.Emails.Sent.Clear();
        _h.Clock.UtcNow = CheckIn("2027-03-08").AddDays(-1);
        await Job.RunAsync();
        _h.Clock.UtcNow = CheckIn("2027-04-05").AddDays(-1);
        await Job.RunAsync();
        Assert.Empty(_h.Emails.Sent);
        Assert.NotNull(s);
    }

    [Fact]
    public async Task Expires_lapsed_holds()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        _h.Clock.Advance(TimeSpan.FromMinutes(39));
        Assert.Equal(0, (await Job.RunAsync()).HoldsExpired);
        _h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await Job.RunAsync()).HoldsExpired);
        Assert.Equal(BookingStatus.Expired, _h.Db.Bookings.Get(r.BookingId)!.Status);
    }

    [Fact]
    public async Task Abandoned_draft_is_reported_once_after_the_threshold()
    {
        await _h.CreateDraftAsync();
        _h.Clock.Advance(TimeSpan.FromHours(2));
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Unfinished"));
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        await Job.RunAsync();
        await Job.RunAsync();
        var email = _h.Emails.WithSubject("Unfinished").Single();
        Assert.Contains("jane@example.com", email.HtmlBody);
        Assert.Contains("Your details", email.HtmlBody);
        Assert.Empty(_h.Db.Customers.All);
    }

    [Fact]
    public async Task Only_the_latest_draft_per_email_is_reported()
    {
        await _h.CreateDraftAsync(start: "2027-03-08");
        _h.Clock.Advance(TimeSpan.FromMinutes(10));
        await _h.CreateDraftAsync(start: "2027-03-15");
        _h.Clock.Advance(TimeSpan.FromHours(3));
        await Job.RunAsync();
        var email = _h.Emails.WithSubject("Unfinished").Single();
        Assert.Contains("15/03/2027", email.HtmlBody);
    }

    [Fact]
    public async Task No_abandoned_report_when_a_booking_for_that_email_became_active()
    {
        await _h.CreateDraftAsync(start: "2027-03-15");
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await _h.BookOnlineAsync(start: "2027-03-08"); // a later draft, converted
        _h.Clock.Advance(TimeSpan.FromHours(3));
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Unfinished"));

        await _h.CreateDraftAsync(start: "2027-04-05", email: "solo@example.com");
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-05-03", "2027-05-07", email: "solo@example.com"));
        _h.Clock.Advance(TimeSpan.FromHours(3));
        await Job.RunAsync();
        Assert.Empty(_h.Emails.WithSubject("Unfinished"));
    }

    [Fact]
    public async Task Drafts_are_deleted_after_retention()
    {
        await _h.CreateDraftAsync();
        await _h.BookOnlineAsync(start: "2027-04-05", email: "other@example.com");
        _h.Clock.Advance(TimeSpan.FromDays(30).Subtract(TimeSpan.FromMinutes(1)));
        await Job.RunAsync();
        Assert.Equal(2, _h.Db.Drafts.All.Count);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, (await Job.RunAsync()).DraftsDeleted);
        Assert.Empty(_h.Db.Drafts.All);
    }
}

public class EmailTemplateTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Staff_new_booking_email_shows_the_frequent_floater_notice_only_for_frequent_floaters()
    {
        await _h.BookOnlineAsync(start: "2027-03-08");
        var first = _h.Emails.WithSubject("New online booking").Single();
        Assert.DoesNotContain("Frequent Floater", first.HtmlBody);
        await _h.BookOnlineAsync(start: "2027-04-05");
        var second = _h.Emails.WithSubject("New online booking").Last();
        Assert.Contains("⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one.", second.HtmlBody);
    }

    [Fact]
    public async Task Bank_placeholders_render_settings_values_or_empty()
    {
        var settings = _h.Get<SettingsService>();
        await settings.UpdateTemplateAsync(EmailTemplateKey.PaymentDue,
            new SaveEmailTemplateRequest("Due {{BookingReference}}", "<p>[{{AccountName}}|{{BSB}}|{{AccountNumber}}]</p>", 1));
        var b = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        await _h.Admin.SendPaymentLinkAsync(b.Booking.Id);
        Assert.Contains("[||]", _h.Emails.Sent.Last().HtmlBody);

        var current = await settings.GetBusinessAsync();
        await settings.UpdateBusinessAsync(current with { AccountName = "Berger & Co", Bsb = "123456", AccountNumber = "12345678" });
        await _h.Admin.SendPaymentLinkAsync(b.Booking.Id);
        Assert.Contains("[Berger &amp; Co|123-456|12345678]", _h.Emails.Sent.Last().HtmlBody);
    }

    [Fact]
    public async Task Saving_a_template_with_an_unknown_placeholder_is_rejected() =>
        await Assert.ThrowsAsync<DomainValidationException>(() => _h.Get<SettingsService>().UpdateTemplateAsync(EmailTemplateKey.PaymentDue,
            new SaveEmailTemplateRequest("Hi", "{{Unknown}}", 1)));

    [Fact]
    public async Task Check_in_time_shows_the_window_and_zone_or_the_custom_time()
    {
        var b = await _h.Admin.CreateAsync(_h.StaffRequest("2027-07-05", "2027-07-09"));
        Assert.Contains("11am – 2pm AEST/NSW", _h.Emails.Sent.Last().HtmlBody);
        var updated = await _h.Admin.UpdateAsync(b.Booking.Id, _h.UpdateRequest(b.Booking, r => r with { CheckInTime = "13:30" }));
        Assert.Equal("1:30pm AEST/NSW", BookingNotifier.CheckInTimeText(_h.Db.Bookings.Get(updated.Booking.Id)!));
    }
}
