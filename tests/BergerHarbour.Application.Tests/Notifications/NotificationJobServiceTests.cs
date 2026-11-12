using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Notifications;

public class NotificationJobServiceTests
{
    private readonly ApplicationHarness h = new();

    private NotificationJobService Job => h.Get<NotificationJobService>();

    private static DateTimeOffset CheckIn(string date) => Sydney.InstantOf(D(date), new TimeOnly(11, 0));

    [Fact]
    public async Task Reminder_at_due_minus_7_days_and_due_email_at_due_then_staff_overdue_once()
    {
        await h.BookOnlineAsync(start: "2027-03-08");
        var due = CheckIn("2027-03-08") - TimeSpan.FromDays(30);
        h.Clock.UtcNow = due - TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1);
        await Job.RunAsync();
        Assert.Equal(0, h.CountEmails("Payment due"));
        h.Clock.UtcNow = due - TimeSpan.FromDays(7);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Equal(1, h.CountEmails("Payment due"));
        h.Clock.UtcNow = due;
        await Job.RunAsync();
        Assert.Equal(2, h.CountEmails("Payment due"));
        Assert.Equal(0, h.CountEmails("Overdue"));
        h.Clock.UtcNow = due + TimeSpan.FromHours(1);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Equal(2, h.CountEmails("Payment due"));
        Assert.Single(h.EmailsTo(StaffEmail), m => m.Subject.Contains("Overdue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_is_sent_once_paid_in_full()
    {
        var booking = await h.BookOnlineAsync(start: "2027-03-08");
        var detail = await h.Staff.GetAsync(booking.Id.Value);
        await h.Staff.AddManualPaymentAsync(detail.Id, new Application.Bookings.Dtos.ManualPaymentRequest(3060, D("2026-11-09"), null, detail.Version));
        var sent = h.Emails.Sent.Count;
        h.Clock.UtcNow = CheckIn("2027-03-08") - TimeSpan.FromDays(10);
        await Job.RunAsync();
        Assert.Equal(sent, h.Emails.Sent.Count);
    }

    [Fact]
    public async Task A_booking_made_35_days_before_gets_no_reminder_but_gets_the_due_email()
    {
        await h.BookOnlineAsync(start: "2026-12-14");
        h.Clock.UtcNow = CheckIn("2026-12-14") - TimeSpan.FromDays(31);
        await Job.RunAsync();
        Assert.Equal(0, h.CountEmails("Payment due"));
        h.Clock.UtcNow = CheckIn("2026-12-14") - TimeSpan.FromDays(30);
        await Job.RunAsync();
        Assert.Equal(1, h.CountEmails("Payment due"));
    }

    [Fact]
    public async Task A_booking_made_after_the_due_date_gets_no_4_or_7_and_its_1_includes_the_payment_link()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2026-11-19", "2026-11-23"));
        var confirmation = h.EmailsTo("john@example.com").Single();
        Assert.Contains("is now due", confirmation.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("/pay/?token=", confirmation.HtmlBody, StringComparison.Ordinal);
        foreach (var hours in new[] { 1, 24, 24 * 9 })
        {
            h.Clock.UtcNow = Start + TimeSpan.FromHours(hours);
            await Job.RunAsync();
        }

        Assert.Equal(0, h.CountEmails("Payment due"));
        Assert.Equal(0, h.CountEmails("Overdue"));
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Pre_hire_instructions_7_days_before_or_at_the_next_run_inside_the_window()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2026-11-12", "2026-11-16", email: "late@example.com"));
        await h.Staff.CreateAsync(h.StaffRequest("2026-12-21", "2026-12-24", email: "early@example.com", lastName: "Early"));
        await Job.RunAsync();
        Assert.Single(h.EmailsTo("late@example.com"), m => m.Subject.Contains("Getting ready", StringComparison.Ordinal));
        Assert.DoesNotContain(h.EmailsTo("early@example.com"), m => m.Subject.Contains("Getting ready", StringComparison.Ordinal));
        h.Clock.UtcNow = CheckIn("2026-12-21") - TimeSpan.FromDays(7);
        await Job.RunAsync();
        await Job.RunAsync();
        Assert.Single(h.EmailsTo("early@example.com"), m => m.Subject.Contains("Getting ready", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Standbys_get_no_emails()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2026-11-12", "2026-11-16", standby: true));
        await Job.RunAsync();
        Assert.Empty(h.Emails.Sent);
    }

    [Fact]
    public async Task Lapsed_holds_are_expired_and_release_the_dates()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        h.Clock.Advance(TimeSpan.FromMinutes(41));
        var result = await Job.RunAsync();
        Assert.Equal(1, result.HoldsExpired);
        Assert.Equal(BookingStatus.Expired, h.Booking(created.BookingId).Status);
    }

    [Fact]
    public async Task Abandoned_drafts_are_reported_once_after_the_threshold()
    {
        await h.CreateDraftAsync(email: "lost@example.com");
        h.Clock.Advance(TimeSpan.FromHours(2));
        await Job.RunAsync();
        Assert.Equal(0, h.CountEmails("Unfinished online booking"));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await Job.RunAsync();
        await Job.RunAsync();
        var email = Assert.Single(h.EmailsTo(StaffEmail));
        Assert.Contains("lost@example.com", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Pacific Blue", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Details", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_latest_draft_per_email_is_reported()
    {
        await h.CreateDraftAsync(email: "twice@example.com");
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await h.CreateDraftAsync(email: "twice@example.com", start: "2027-03-15");
        h.Clock.Advance(TimeSpan.FromHours(3));
        await Job.RunAsync();
        var email = Assert.Single(h.EmailsTo(StaffEmail));
        Assert.Contains("15/03/2027", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_report_when_a_booking_for_that_email_became_active_after_the_draft()
    {
        await h.CreateDraftAsync(email: "jane@example.com", start: "2027-03-15");
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await h.BookOnlineAsync(email: "jane@example.com");
        h.Clock.Advance(TimeSpan.FromHours(3));
        await Job.RunAsync();
        Assert.DoesNotContain(h.EmailsTo(StaffEmail), m => m.Subject.Contains("Unfinished", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Drafts_are_deleted_after_retention()
    {
        await h.CreateDraftAsync();
        h.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));
        var result = await Job.RunAsync();
        Assert.Equal(1, result.DraftsDeleted);
        Assert.Empty(h.Db.Drafts.All);
    }

    [Fact]
    public async Task A_hold_staff_confirm_days_later_gets_no_catch_up_reminder_but_gets_the_due_email()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync(start: "2027-02-15"), start: "2027-02-15"));
        var due = CheckIn("2027-02-15") - TimeSpan.FromDays(30);
        h.Clock.UtcNow = due - TimeSpan.FromDays(6);
        var expired = await h.Staff.GetAsync(created.BookingId);
        await h.Staff.AddManualPaymentAsync(expired.Id, new Application.Bookings.Dtos.ManualPaymentRequest(1000, Sydney.DateOf(h.Clock.UtcNow), null, expired.Version));
        await Job.RunAsync();
        Assert.Equal(0, h.CountEmails("Payment due"));
        h.Clock.UtcNow = due;
        await Job.RunAsync();
        Assert.Equal(1, h.CountEmails("Payment due"));
        h.Clock.UtcNow = due + TimeSpan.FromHours(1);
        await Job.RunAsync();
        Assert.Single(h.EmailsTo(StaffEmail), m => m.Subject.Contains("Overdue", StringComparison.Ordinal));
    }
}
