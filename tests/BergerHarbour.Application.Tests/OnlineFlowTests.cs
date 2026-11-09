using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Harness;

namespace BergerHarbour.Application.Tests;

public class OnlineFlowTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Draft_creation_requires_turnstile_and_never_creates_a_customer()
    {
        _h.Turnstile.Pass = false;
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => _h.CreateDraftAsync());
        Assert.Contains("turnstileToken", e.Errors.Keys);
        _h.Turnstile.Pass = true;
        await _h.CreateDraftAsync();
        Assert.Empty(_h.Db.Customers.All);
    }

    [Fact]
    public async Task Hold_is_pending_without_customer_or_reference_and_checkout_charges_the_deposit()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        var hold = _h.Db.Bookings.Get(r.BookingId)!;
        Assert.Equal(BookingStatus.PendingPayment, hold.Status);
        Assert.Null(hold.Reference);
        Assert.Null(hold.CustomerId);
        Assert.Equal("jane@example.com", hold.CustomerDetails!.Email);
        Assert.Equal(_h.Clock.UtcNow.AddMinutes(40), hold.HoldExpiresAt);
        Assert.Equal(1000m, _h.Payments.Created.Single().Amount);
        Assert.Equal(_h.Clock.UtcNow.AddMinutes(30), _h.Payments.Created.Single().ExpiresAt);
        Assert.Empty(_h.Db.Customers.All);
        Assert.Equal(DraftStep.Payment, _h.Db.Drafts.Get(draft)!.LastStepReached);
    }

    [Fact]
    public async Task Prices_sent_by_the_browser_are_ignored_and_addons_priced_on_the_server()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft, addons: [new("outboard-motor", 1), new("welcome-hamper", 1)]));
        var hold = _h.Db.Bookings.Get(r.BookingId)!;
        Assert.Equal(4060m, hold.HirePrice);
        Assert.Equal(95m, hold.AddonLines.Single(l => l.AddonId == "outboard-motor").UnitPrice);
        Assert.Null(hold.AddonLines.Single(l => l.AddonId == "welcome-hamper").UnitPrice);
        Assert.Equal(4155m, hold.TotalPrice);
    }

    [Fact]
    public async Task Deposit_activates_creates_the_customer_converts_the_draft_and_emails()
    {
        var booking = await _h.BookOnlineAsync();
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal("SMITH-2703080", booking.Reference);
        Assert.Equal(_h.Clock.UtcNow, booking.ActivatedAt);
        var customer = _h.Db.Customers.All.Single();
        Assert.Equal(customer.Id, booking.CustomerId);
        Assert.Equal("+61412345678", customer.MobileNumber);
        Assert.True(_h.Db.Drafts.Get(booking.BookingDraftId!)!.IsConverted);
        Assert.Single(_h.Emails.WithSubject("is confirmed"));
        Assert.Single(_h.Emails.WithSubject("New online booking"));
        Assert.Equal(PaymentStatus.DepositPaid, booking.PaymentStatus);
    }

    [Fact]
    public async Task Webhook_replay_is_idempotent()
    {
        var booking = await _h.BookOnlineAsync();
        await _h.PayDepositAsync(booking.Id); // same PaymentIntent
        var again = _h.Db.Bookings.Get(booking.Id)!;
        Assert.Single(again.Payments);
        Assert.Single(_h.Emails.WithSubject("is confirmed"));
        Assert.Single(_h.Db.Customers.All);
        Assert.Equal(1, _h.References.Counters["SMITH-270308"]);
    }

    [Fact]
    public async Task Existing_customer_is_updated_but_comments_and_secondary_mobile_are_kept()
    {
        var staff = await _h.Admin.CreateAsync(_h.StaffRequest("2027-05-03", "2027-05-07", email: "jane@example.com", customerComments: "Prefers upper deck"));
        var customer = _h.Db.Customers.All.Single();
        customer.UpdateContactDetails("Janey", "Smith", "0400 111 222", "0499 999 999");
        await _h.Db.SaveAsync(customer);
        await _h.BookOnlineAsync();
        var updated = _h.Db.Customers.All.Single();
        Assert.Equal("Jane", updated.FirstName);
        Assert.Equal("+61412345678", updated.MobileNumber);
        Assert.Equal("+61499999999", updated.SecondaryMobileNumber);
        Assert.Equal("Prefers upper deck", updated.Comments);
        Assert.NotNull(staff);
    }

    [Fact]
    public async Task Paid_but_expired_keeps_the_payment_assigns_a_reference_and_creates_no_customer()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        _h.Clock.Advance(TimeSpan.FromMinutes(45));
        await _h.Get<Notifications.NotificationJob>().RunAsync(); // expires the hold
        var taken = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        await _h.PayDepositAsync(r.BookingId);
        var b = _h.Db.Bookings.Get(r.BookingId)!;
        Assert.Equal(BookingStatus.Expired, b.Status);
        Assert.Single(b.Payments);
        Assert.Equal("SMITH-2703081", b.Reference); // staff booking took …0
        Assert.Null(b.CustomerId);
        Assert.Single(_h.Db.Customers.All); // only the staff booking's customer
        Assert.Empty(_h.Emails.WithSubject("New online booking"));
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task Deposit_after_the_hold_lapsed_still_activates_when_the_dates_are_free()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        _h.Clock.Advance(TimeSpan.FromMinutes(45));
        await _h.Get<Notifications.NotificationJob>().RunAsync();
        await _h.PayDepositAsync(r.BookingId);
        Assert.Equal(BookingStatus.Active, _h.Db.Bookings.Get(r.BookingId)!.Status);
    }

    [Fact]
    public async Task Expired_and_released_holds_get_no_reference_and_use_no_suffix_and_create_no_customer()
    {
        var d1 = await _h.CreateDraftAsync();
        var held = await _h.Online.CreateBookingAsync(_h.BookingRequest(d1));
        await _h.Online.ReleaseAsync(held.BookingId, d1);
        var d2 = await _h.CreateDraftAsync();
        var lapsed = await _h.Online.CreateBookingAsync(_h.BookingRequest(d2));
        await _h.Get<StripeWebhookService>().HandleExpiredAsync(new CheckoutExpired(_h.Db.Bookings.Get(lapsed.BookingId)!.StripeCheckoutSessionId!, lapsed.BookingId));
        Assert.All(_h.Db.Bookings.All, b => Assert.Null(b.Reference));
        Assert.All(_h.Db.Bookings.All, b => Assert.Equal(BookingStatus.Expired, b.Status));
        Assert.Empty(_h.References.Counters);
        Assert.Empty(_h.Db.Customers.All);
        var booked = await _h.BookOnlineAsync();
        Assert.Equal("SMITH-2703080", booked.Reference);
    }

    [Fact]
    public async Task Release_expires_the_session_and_frees_the_dates_and_is_idempotent()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Online.ReleaseAsync(r.BookingId, "wrong-draft"));
        await _h.Online.ReleaseAsync(r.BookingId, draft);
        await _h.Online.ReleaseAsync(r.BookingId, draft);
        Assert.Equal([r.SessionId], _h.Payments.Expired);
        Assert.Equal(BookingStatus.Expired, _h.Db.Bookings.Get(r.BookingId)!.Status);
        var again = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft)); // dates are free again
        Assert.NotEqual(r.BookingId, again.BookingId);
    }

    [Fact]
    public async Task Release_after_payment_returns_conflict()
    {
        var booking = await _h.BookOnlineAsync();
        await Assert.ThrowsAsync<ConflictException>(() => _h.Online.ReleaseAsync(booking.Id, booking.BookingDraftId!));
    }

    [Fact]
    public async Task Release_when_stripe_already_completed_returns_conflict()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        _h.Payments.SessionStatus[r.SessionId] = "complete";
        await Assert.ThrowsAsync<ConflictException>(() => _h.Online.ReleaseAsync(r.BookingId, draft));
    }

    [Fact]
    public async Task Taken_dates_return_conflict()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-10", "2027-03-11"));
        var draft = await _h.CreateDraftAsync();
        var e = await Assert.ThrowsAsync<ConflictException>(() => _h.Online.CreateBookingAsync(_h.BookingRequest(draft)));
        Assert.Equal("Those dates are no longer available", e.Message);
    }

    [Fact]
    public async Task Updating_a_draft_with_another_boat_lets_the_booking_use_that_boat()
    {
        _h.Activate("misty-blue");
        var draft = await _h.CreateDraftAsync();
        await _h.Online.UpdateDraftAsync(draft, new UpdateDraftRequest("misty-blue", PeriodType.Midweek, D("2027-03-08"), 2, 0, "Jane", "Smith",
            "jane@example.com", "0412345678", [], DraftStep.Addons));
        Assert.Equal(_h.Boat("misty-blue").Id, _h.Db.Drafts.Get(draft)!.BoatId);
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft, "misty-blue"));
        Assert.Equal(_h.Boat("misty-blue").Id, _h.Db.Bookings.Get(r.BookingId)!.BoatId);
    }

    [Fact]
    public async Task Converted_drafts_cannot_be_updated()
    {
        var booking = await _h.BookOnlineAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Online.UpdateDraftAsync(booking.BookingDraftId!, new UpdateDraftRequest("pacific-blue",
            PeriodType.Midweek, D("2027-03-08"), 2, 0, "Jane", "Smith", "jane@example.com", "0412345678", [], DraftStep.Summary)));
    }

    [Fact]
    public async Task Stripe_failure_releases_the_hold()
    {
        _h.Payments.Fail = true;
        var draft = await _h.CreateDraftAsync();
        await Assert.ThrowsAsync<Common.ExternalServiceException>(() => _h.Online.CreateBookingAsync(_h.BookingRequest(draft)));
        Assert.Equal(BookingStatus.Expired, _h.Db.Bookings.All.Single().Status);
    }

    [Fact]
    public async Task Status_requires_the_matching_session_and_shows_the_reference_once_active()
    {
        var draft = await _h.CreateDraftAsync();
        var r = await _h.Online.CreateBookingAsync(_h.BookingRequest(draft));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Online.GetStatusAsync(r.BookingId, "cs_other"));
        Assert.Null((await _h.Online.GetStatusAsync(r.BookingId, r.SessionId)).Reference);
        await _h.PayDepositAsync(r.BookingId);
        var status = await _h.Online.GetStatusAsync(r.BookingId, r.SessionId);
        Assert.Equal(BookingStatus.Active, status.Status);
        Assert.Equal("SMITH-2703080", status.Reference);
    }

    [Fact]
    public async Task Quote_includes_rooming_warning_schedule_and_checkout_amount()
    {
        var q = await _h.Online.QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Week, D("2027-03-05"), 8, 1, [new("outboard-motor", 1)]));
        Assert.Equal(5850m, q.HirePrice);
        Assert.Equal(5980m, q.TotalPrice);
        Assert.Equal(1000m, q.AmountDueAtCheckout);
        Assert.NotNull(q.RoomingWarning);
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.Balance], q.PaymentSchedule.Select(s => s.Kind));
        Assert.Equal(D("2027-02-03"), q.PaymentSchedule[1].DueDate);
    }

    [Fact]
    public async Task Balance_payments_record_and_send_a_receipt_once()
    {
        var booking = await _h.BookOnlineAsync();
        var webhook = _h.Get<StripeWebhookService>();
        var e = new CheckoutCompleted("cs_pay", booking.Id, CheckoutPurpose.Payment, "pi_balance", 3060, _h.Clock.UtcNow);
        await webhook.HandleCompletedAsync(e);
        await webhook.HandleCompletedAsync(e);
        Assert.Equal(PaymentStatus.FullyPaid, _h.Db.Bookings.Get(booking.Id)!.PaymentStatus);
        Assert.Single(_h.Emails.WithSubject("Payment received"));
    }

    [Fact]
    public async Task Pay_page_shows_amount_due_now_and_refuses_when_nothing_is_owing()
    {
        var booking = await _h.BookOnlineAsync();
        var token = _h.Get<Common.PaymentLinkTokens>().TokenFor(booking.Id);
        var pay = _h.Get<PaymentLinkService>();
        var details = await pay.GetAsync(token);
        Assert.Equal(3060m, details.AmountDueNow); // nothing due yet: the amount owing
        var checkout = await pay.CreateCheckoutAsync(token);
        Assert.Equal(CheckoutPurpose.Payment, _h.Payments.Created.Last().Purpose);
        Assert.NotNull(checkout.ClientSecret);
        await _h.Get<StripeWebhookService>().HandleCompletedAsync(new CheckoutCompleted("cs", booking.Id, CheckoutPurpose.Payment, "pi_x", 3060, _h.Clock.UtcNow));
        await Assert.ThrowsAsync<DomainValidationException>(() => pay.CreateCheckoutAsync(token));
        await Assert.ThrowsAsync<NotFoundException>(() => pay.GetAsync("not-a-token"));
    }
}
