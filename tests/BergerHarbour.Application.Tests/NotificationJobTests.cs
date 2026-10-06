using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class NotificationJobTests
{
    private static async Task<(TestApp App, BookingDetailDto Booking)> StaffBookingAsync(DateOnly? start = null,
        bool standby = false, decimal paid = 1000m, NotificationOptions? options = null, DateTimeOffset? now = null,
        int nights = 4, decimal hirePrice = 4060m)
    {
        var app = await TestApp.CreateAsync(online: false, notifications: options, now: now);
        var s = start ?? app.MondayAfter(60);
        var admin = app.Get<AdminBookingService>();
        var booking = await admin.CreateAsync(new AdminBookingRequest("pacific-blue", PeriodType.Custom, s, s.AddDays(nights), 4,
            "jane@example.com", "Jane Citizen", null, standby, null, hirePrice));
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
        var cancelled = await admin.CreateAsync(new AdminBookingRequest("pacific-blue", PeriodType.Custom, other, other.AddDays(3), 2,
            "bob@example.com", "Bob", null, false, null, 3000m));
        await admin.UpdateAsync(cancelled.Id, new AdminBookingUpdateRequest(cancelled.Version, "pacific-blue", PeriodType.Custom,
            other, other.AddDays(3), 2, "bob@example.com", "Bob", null, false, null, 3000m, BookingStatus.Cancelled));
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
    public async Task Booking_entered_after_the_due_date_gets_due_and_overdue_immediately()
    {
        var (app, booking) = await StaffBookingAsync(start: TestApp.DefaultNow.AddDays(20).ToSydneyDate());
        await Run(app);
        Assert.Equal(2, app.Emails.Sent.Count);
        Assert.Single(app.Emails.Sent, m => m.To == "jane@example.com");
        Assert.Single(app.Emails.Sent, m => m.To == "staff@example.test");
        await Run(app);
        Assert.Equal(2, app.Emails.Sent.Count);
        Assert.NotNull(booking);
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
