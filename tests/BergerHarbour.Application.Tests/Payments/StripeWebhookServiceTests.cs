using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Payments;

public class StripeWebhookServiceTests
{
    private readonly ApplicationHarness h = new();

    private StripeWebhookService Service => h.Get<StripeWebhookService>();

    [Fact]
    public async Task Deposit_activates_assigns_the_reference_creates_the_customer_converts_the_draft_and_sends_1_and_2()
    {
        var booking = await h.BookOnlineAsync(start: "2027-01-11");
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal("SMITH-2701110", booking.Reference!.Value);
        Assert.Equal(h.Clock.UtcNow, booking.ActivatedAt);
        var customer = h.Db.Customers.All.Single();
        Assert.Equal(customer.Id, booking.CustomerId);
        Assert.Equal("+61412345678", customer.MobileNumber!.Value);
        Assert.True(h.Db.Drafts.Get(booking.BookingDraftId!.Value)!.IsConverted);
        Assert.Single(h.EmailsTo("jane@example.com"), m => m.Subject.Contains("is confirmed", StringComparison.Ordinal));
        Assert.Single(h.EmailsTo(StaffEmail), m => m.Subject.Contains("New online booking", StringComparison.Ordinal));
        Assert.Equal(1000m, booking.AmountPaid);
    }

    [Fact]
    public async Task Replaying_the_webhook_is_idempotent()
    {
        var booking = await h.BookOnlineAsync();
        await h.PayDepositAsync(booking.Id.Value);
        var replayed = h.Booking(booking.Id.Value);
        Assert.Single(replayed.Payments);
        Assert.Single(h.Db.Customers.All);
        Assert.Equal(2, h.Emails.Sent.Count);
        Assert.Equal([1], h.References.Counters.Values);
    }

    [Fact]
    public async Task Rebooking_updates_name_and_mobile_but_never_comments_secondary_mobile_or_address()
    {
        var first = await h.BookOnlineAsync(start: "2027-03-08");
        var detail = await h.Staff.GetAsync(first.Id.Value);
        await h.Staff.UpdateAsync(detail.Id, UpdateRequest(detail, r => r with
        {
            CustomerComments = "VIP", SecondaryMobile = "+61412000999", Address = "1 River Rd",
        }));
        await h.BookOnlineAsync(start: "2027-03-15", lastName: "Smith-Jones");
        var customer = h.Db.Customers.All.Single();
        Assert.Equal("Smith-Jones", customer.LastName);
        Assert.Equal("VIP", customer.Comments);
        Assert.Equal("+61412000999", customer.SecondaryMobileNumber!.Value);
        Assert.Equal("1 River Rd", customer.Address);
    }

    [Fact]
    public async Task Paid_after_the_hold_lapsed_with_the_dates_taken_keeps_the_payment_assigns_a_reference_and_creates_no_customer()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        await h.PayDepositAsync(created.BookingId);
        var booking = h.Booking(created.BookingId);
        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.Equal("SMITH-2703080", booking.Reference!.Value);
        Assert.Single(booking.Payments);
        Assert.DoesNotContain(h.Db.Customers.All, c => c.Email.Value == "jane@example.com");
        Assert.Empty(h.EmailsTo("jane@example.com"));
    }

    [Fact]
    public async Task Paid_after_the_hold_lapsed_with_the_dates_still_free_activates()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.PayDepositAsync(created.BookingId);
        Assert.Equal(BookingStatus.Active, h.Booking(created.BookingId).Status);
    }

    [Fact]
    public async Task An_expired_hold_never_creates_or_changes_a_customer()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        await Service.HandleExpiredAsync(new CheckoutSessionExpired(created.SessionId, new BookingId(created.BookingId)));
        Assert.Equal(BookingStatus.Expired, h.Booking(created.BookingId).Status);
        Assert.Empty(h.Db.Customers.All);
        Assert.Empty(h.References.Counters);
    }

    [Fact]
    public async Task Activation_supersedes_overlapping_standbys_and_alerts_staff()
    {
        var standby = await h.Staff.CreateAsync(h.StaffRequest("2027-03-09", "2027-03-11", standby: true, email: "credit@example.com"));
        await h.BookOnlineAsync();
        Assert.Equal(BookingStatus.Superseded, h.Booking(standby.Booking.Id).Status);
        Assert.Single(h.EmailsTo(StaffEmail), m => m.Subject.Contains("Stand-by booking superseded", StringComparison.Ordinal));
        Assert.Empty(h.EmailsTo("credit@example.com"));
    }

    [Fact]
    public async Task A_later_payment_records_it_and_sends_5_once()
    {
        var booking = await h.BookOnlineAsync();
        var completed = new CheckoutSessionCompleted("cs_pay", booking.Id, CheckoutPurpose.Payment, "pi_balance", 500, h.Clock.UtcNow);
        await Service.HandleCompletedAsync(completed);
        await Service.HandleCompletedAsync(completed);
        Assert.Equal(1500m, h.Booking(booking.Id.Value).AmountPaid);
        Assert.Equal(1, h.CountEmails("Payment received"));
    }

    [Fact]
    public async Task A_bad_signature_is_rejected() =>
        await Assert.ThrowsAsync<InvalidWebhookException>(() => Service.HandleAsync("{}", "bad"));

    [Fact]
    public async Task Requested_addons_appear_in_the_confirmation()
    {
        await h.BookOnlineAsync(addons: [new AddonRequestDto("welcome-hamper", 1), new AddonRequestDto("yabby-pumps", 1)]);
        var body = h.EmailsTo("jane@example.com").Single().HtmlBody;
        Assert.True(body.IndexOf("Yabby pumps", StringComparison.Ordinal) < body.IndexOf("Welcome hamper", StringComparison.Ordinal));
        Assert.Contains("price on request", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_draft_is_marked_converted()
    {
        var booking = await h.BookOnlineAsync();
        var draft = h.Db.Drafts.Get(booking.BookingDraftId!.Value)!;
        Assert.Equal(booking.Id, draft.ConvertedBookingId);
        Assert.Equal(DraftStep.Payment, draft.Contents.LastStepReached);
    }
}
