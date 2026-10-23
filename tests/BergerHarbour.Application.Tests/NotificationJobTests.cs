using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class NotificationJobTests
{
    private static AdminBookingRequest StaffRequest(DateOnly start, int nights = 4, bool standby = false,
        decimal hirePrice = 4060m, string email = "jane@example.com", string name = "Jane Citizen") =>
        new("pacific-blue", PeriodType.Custom, start, start.AddDays(nights), 3, 1, null, null, email, name, null, null,
            standby, null, hirePrice, null);

    private static async Task<(TestApp App, BookingDetailDto Booking)> StaffBookingAsync(DateOnly? start = null,
        bool standby = false, decimal paid = 1000m, NotificationOptions? options = null, DateTimeOffset? now = null,
        int nights = 4, decimal hirePrice = 4060m)
    {
        var app = await TestApp.CreateAsync(online: false, notifications: options, now: now);
        var s = start ?? app.MondayAfter(60);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(StaffRequest(s, nights, standby, hirePrice));
        if (paid > 0)
        {
            booking = await admin.AddManualPaymentAsync(booking.Id,
                new ManualPaymentRequest(booking.Version, paid, app.Today, "Bank transfer"));
        }

        app.Emails.Sent.Clear();
        return (app, booking);
    }

    private static Task<NotificationJobResult> Run(TestApp app) => app.Get<NotificationJob>().RunAsync();

    private static string[] Subjects(TestApp app) => app.Emails.Sent.Select(m => $"{m.To}|{m.Subject}").ToArray();

    [Fact]
    public async Task Standard_schedule_reminder_due_overdue_and_pre_hire_each_sent_once()
    {
        var (app, booking) = await StaffBookingAsync();
        var checkIn = SydneyTime.CheckInInstant(booking.StartDate);
        var due = checkIn.AddDays(-30);

        await Run(app);
        Assert.Empty(app.Emails.Sent);

        app.Clock.UtcNow = due.AddDays(-7).AddMinutes(-1);
        await Run(app);
        Assert.Empty(app.Emails.Sent);

        app.Clock.UtcNow = due.AddDays(-7);
        await Run(app);
        await Run(app);
        var reminder = Assert.Single(app.Emails.Sent);
        Assert.Equal("jane@example.com", reminder.To);
        Assert.Contains("Payment due", reminder.Subject);
        Assert.Contains("$3,060.00", reminder.HtmlBody);
        Assert.Contains("/pay/?token=", reminder.HtmlBody);
        app.Emails.Sent.Clear();

        app.Clock.UtcNow = due;
        await Run(app);
        Assert.Single(app.Emails.Sent); // the due-date email; not yet overdue
        app.Emails.Sent.Clear();

        app.Clock.UtcNow = due.AddHours(1);
        await Run(app);
        await Run(app);
        var overdue = Assert.Single(app.Emails.Sent);
        Assert.Equal("staff@example.test", overdue.To);
        Assert.Contains("Overdue balance", overdue.Subject);
        app.Emails.Sent.Clear();

        app.Clock.UtcNow = checkIn.AddDays(-7);
        await Run(app);
        await Run(app);
        var preHire = Assert.Single(app.Emails.Sent);
        Assert.Contains("Getting ready", preHire.Subject);

        var stored = await app.BookingAsync(booking.Reference);
        Assert.Equal(
            ["PaymentDue:Balance:Reminder", "PaymentDue:Balance:Due", "Overdue:Balance", "PreHire"],
            stored.SentNotifications.Select(n => n.Key).Where(k => k != NotificationKeys.BookingConfirmed && !k.StartsWith("PaymentReceived")));
    }

    [Fact]
    public async Task Paid_bookings_only_get_pre_hire_instructions()
    {
        var (app, booking) = await StaffBookingAsync(paid: 4060m);
        app.Clock.UtcNow = SydneyTime.CheckInInstant(booking.StartDate).AddDays(-6);
        await Run(app);
        Assert.Contains("Getting ready", Assert.Single(app.Emails.Sent).Subject);
    }

    [Fact]
    public async Task Standby_and_cancelled_bookings_get_nothing()
    {
        var (app, standby) = await StaffBookingAsync(standby: true, paid: 0);
        var admin = app.Get<AdminBookingService>();
        var other = app.MondayAfter(90);
        var cancelled = await admin.CreateAsync(StaffRequest(other, 3, hirePrice: 3000m, email: "bob@example.com", name: "Bob"));
        await admin.UpdateAsync(cancelled.Id, AdminBookingTests.Update(cancelled, BookingStatus.Cancelled));
        app.Emails.Sent.Clear();

        foreach (var at in new[] { 40, 20, 5, 1 })
        {
            app.Clock.UtcNow = SydneyTime.CheckInInstant(standby.StartDate).AddDays(-at);
            await Run(app);
        }

        app.Clock.UtcNow = SydneyTime.CheckInInstant(other).AddDays(-1);
        await Run(app);
        Assert.Empty(app.Emails.Sent);
    }

    [Fact]
    public async Task Booking_made_after_the_balance_fell_due_gets_the_link_in_its_confirmation_and_no_payment_emails()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = TestApp.DefaultNow.AddDays(10).ToSydneyDate();
        var booking = await app.Get<AdminBookingService>().CreateAsync(StaffRequest(start));

        var confirmation = Assert.Single(app.Emails.Sent);
        Assert.Contains("The balance of $4,060.00 is now due", confirmation.HtmlBody);
        Assert.Contains("/pay/?token=", confirmation.HtmlBody);
        app.Emails.Sent.Clear();

        foreach (var hours in new[] { 0, 1, 24, 48 })
        {
            app.Clock.UtcNow = TestApp.DefaultNow.AddHours(hours);
            await Run(app);
        }

        Assert.Empty(app.Emails.Sent); // no #4 and never #7 for a milestone already due at creation
        Assert.DoesNotContain((await app.BookingAsync(booking.Reference)).SentNotifications, n => n.Key.StartsWith("Overdue"));

        app.Clock.UtcNow = SydneyTime.CheckInInstant(start).AddDays(-7);
        await Run(app);
        Assert.Contains("Getting ready", Assert.Single(app.Emails.Sent).Subject);
    }

    [Fact]
    public async Task Booking_made_35_days_ahead_skips_the_passed_reminder_but_gets_the_due_email()
    {
        var app = await TestApp.CreateAsync(online: false);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(StaffRequest(TestApp.DefaultNow.AddDays(35).ToSydneyDate()));
        Assert.DoesNotContain("is now due", Assert.Single(app.Emails.Sent).HtmlBody);
        await admin.AddManualPaymentAsync(booking.Id, new ManualPaymentRequest(booking.Version, 1000m, app.Today, null));
        app.Emails.Sent.Clear();
        var due = SydneyTime.CheckInInstant(booking.StartDate).AddDays(-30);

        await Run(app);
        Assert.Empty(app.Emails.Sent); // the 7-day reminder fell before the booking was made

        app.Clock.UtcNow = due;
        await Run(app);
        Assert.Contains("Payment due", Assert.Single(app.Emails.Sent).Subject);
        app.Emails.Sent.Clear();

        app.Clock.UtcNow = due.AddHours(1);
        await Run(app);
        Assert.Contains("Overdue balance", Assert.Single(app.Emails.Sent).Subject);
    }

    [Fact]
    public async Task Booking_made_inside_the_pre_hire_window_gets_instructions_at_the_next_run()
    {
        var (app, _) = await StaffBookingAsync(start: TestApp.DefaultNow.AddDays(3).ToSydneyDate(), paid: 4060m);
        await Run(app);
        Assert.Contains("Getting ready", Assert.Single(app.Emails.Sent).Subject);
    }

    // ---- Abandoned online bookings (#10) -------------------------------------------------------------------------

    private static readonly BookingOptions DraftTiming = new()
    {
        AbandonedDraftAfter = TimeSpan.FromHours(2),
        DraftRetention = TimeSpan.FromDays(30),
    };

    [Fact]
    public async Task Abandoned_draft_is_reported_to_staff_once_after_the_threshold()
    {
        var app = await TestApp.CreateAsync(booking: DraftTiming);
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);
        await app.Get<OnlineBookingService>().UpdateDraftAsync(draftId, new UpdateBookingDraftRequest("pacific-blue",
            PeriodType.Midweek, start, 4, 2, "Jane Citizen", "jane@example.com", "0412 345 678", [new("ice", 2), new("outboard-motor", 1)], BookingStep.Addons));

        app.Clock.Advance(TimeSpan.FromHours(2));
        await Run(app);
        Assert.Empty(app.Emails.Sent);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await Run(app);
        await Run(app);
        var alert = Assert.Single(app.Emails.Sent);
        Assert.Equal("staff@example.test", alert.To);
        Assert.Contains("Unfinished online booking", alert.Subject);
        foreach (var expected in new[] { "Jane Citizen", "jane@example.com", "0412 345 678", "Pacific Blue", "4 / 2", "Ice &#215; 2",
                     "Outboard motor", "Add-ons" })
        {
            Assert.Contains(expected, alert.HtmlBody);
        }

        Assert.NotNull(BookingDraft.FromSnapshot(Assert.Single(app.Store.BookingDrafts.All())).AbandonedNotifiedAt);
        Assert.Empty(app.Store.Customers.All());
    }

    [Fact]
    public async Task Only_the_latest_draft_per_email_is_reported()
    {
        var app = await TestApp.CreateAsync(booking: DraftTiming);
        await app.DraftAsync(app.MondayAfter(40));
        app.Clock.Advance(TimeSpan.FromMinutes(10));
        await app.DraftAsync(app.MondayAfter(50), " JANE@example.com ");

        app.Clock.Advance(TimeSpan.FromHours(3));
        await Run(app);
        Assert.Single(app.Emails.Sent);
    }

    [Fact]
    public async Task No_alert_once_that_email_has_booked_and_paid()
    {
        var app = await TestApp.CreateAsync(booking: DraftTiming);
        var start = app.MondayAfter(40);
        await app.DraftAsync(start); // abandoned attempt
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        var draftId = await app.DraftAsync(app.MondayAfter(50));
        var result = await app.Get<OnlineBookingService>().CreateAsync(new CreateOnlineBookingRequest(draftId, "pacific-blue",
            PeriodType.Midweek, app.MondayAfter(50), 2, 0, "Jane Citizen", "jane@example.com", "0412 345 678", [], false, null, true));
        var hold = await app.BookingAsync(result.Reference);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(new CheckoutSessionCompleted("evt", hold.StripeCheckoutSessionId!,
            hold.Id.Value, CheckoutPurpose.Deposit, "pi_1", 1000m, app.Clock.UtcNow));
        app.Emails.Sent.Clear();

        app.Clock.Advance(TimeSpan.FromHours(3));
        await Run(app);
        Assert.Empty(app.Emails.Sent);
    }

    [Fact]
    public async Task An_unpaid_hold_still_counts_as_abandoned()
    {
        var app = await TestApp.CreateAsync(booking: DraftTiming);
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);
        await app.Get<OnlineBookingService>().CreateAsync(new CreateOnlineBookingRequest(draftId, "pacific-blue",
            PeriodType.Midweek, start, 2, 0, "Jane Citizen", "jane@example.com", "0412 345 678", [], false, null, true));

        app.Clock.Advance(TimeSpan.FromHours(3));
        await Run(app);
        var alert = Assert.Single(app.Emails.Sent, m => m.Subject.Contains("Unfinished"));
        Assert.Contains("Payment", alert.HtmlBody);
    }

    [Fact]
    public async Task Drafts_are_deleted_after_the_retention_period()
    {
        var app = await TestApp.CreateAsync(booking: DraftTiming);
        await app.DraftAsync(app.MondayAfter(40));
        app.Clock.Advance(TimeSpan.FromDays(30));
        var result = await Run(app);
        Assert.Equal(0, result.DraftsDeleted);
        Assert.Single(app.Store.BookingDrafts.All());

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, (await Run(app)).DraftsDeleted);
        Assert.Empty(app.Store.BookingDrafts.All());
    }

    [Fact]
    public async Task Extended_schedule_reminds_for_first_and_final_instalments()
    {
        // Christmas 2026 starts 20/12; book 22/12 → 29/12 well ahead (clock in early July 2026).
        var (app, booking) = await StaffBookingAsync(start: new DateOnly(2026, 12, 22), nights: 7, hirePrice: 7000m,
            now: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(PaymentSchedule.Extended, booking.PaymentSchedule);
        var checkIn = SydneyTime.CheckInInstant(booking.StartDate);

        app.Clock.UtcNow = checkIn.AddDays(-127);
        await Run(app);
        var first = Assert.Single(app.Emails.Sent);
        Assert.Contains("$2,500.00", first.HtmlBody); // 50% of 7000 = 3500 − 1000 paid
        app.Emails.Sent.Clear();

        app.Clock.UtcNow = checkIn.AddDays(-97);
        await Run(app);
        Assert.Equal(3, app.Emails.Sent.Count); // first: due + overdue; final: reminder

        var keys = (await app.BookingAsync(booking.Reference)).SentNotifications.Select(n => n.Key).ToList();
        Assert.Contains("PaymentDue:FirstInstalment:Reminder", keys);
        Assert.Contains("PaymentDue:FirstInstalment:Due", keys);
        Assert.Contains("Overdue:FirstInstalment", keys);
        Assert.Contains("PaymentDue:Final:Reminder", keys);
    }

    [Fact]
    public async Task Short_offsets_from_configuration_drive_the_schedule()
    {
        var options = new NotificationOptions
        {
            BalanceDueBeforeHire = TimeSpan.FromHours(2),
            PaymentReminderBeforeDue = TimeSpan.FromMinutes(30),
            PreHireInstructionsBeforeHire = TimeSpan.FromHours(1),
        };
        var tomorrow = TestApp.DefaultNow.ToSydneyDate().AddDays(1);
        var (app, booking) = await StaffBookingAsync(start: tomorrow, options: options);
        var checkIn = SydneyTime.CheckInInstant(booking.StartDate);

        app.Clock.UtcNow = checkIn.AddHours(-2).AddMinutes(-31);
        await Run(app);
        Assert.Empty(app.Emails.Sent);

        app.Clock.UtcNow = checkIn.AddHours(-2).AddMinutes(-30);
        await Run(app);
        Assert.Single(app.Emails.Sent);

        app.Clock.UtcNow = checkIn.AddHours(-1);
        await Run(app);
        Assert.Equal(4, app.Emails.Sent.Count); // + due, overdue, pre-hire
    }

    [Fact]
    public async Task Bookings_that_started_in_the_past_are_ignored()
    {
        var (app, booking) = await StaffBookingAsync(start: TestApp.DefaultNow.ToSydneyDate().AddDays(3));
        app.Clock.UtcNow = SydneyTime.CheckInInstant(booking.StartDate).AddDays(1);
        await Run(app);
        Assert.Empty(app.Emails.Sent);
    }
}

internal static class DateExtensions
{
    public static DateOnly ToSydneyDate(this DateTimeOffset instant) => SydneyTime.Today(instant);
}
