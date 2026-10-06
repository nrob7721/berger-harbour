using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class OnlineBookingFlowTests
{
    private static CreateOnlineBookingRequest Request(DateOnly start, int guests = 6, string email = "jane@example.com",
        string? roomingText = null) =>
        new("pacific-blue", PeriodType.Midweek, start, guests, "Jane Citizen", email, "0400 123 456",
            [new BookingAddonRequest("outboard-motor", 1), new BookingAddonRequest("welcome-hamper", 1)],
            roomingText is not null, roomingText, false, true, "turnstile-ok");

    private static CheckoutSessionCompleted Paid(Booking booking, decimal amount, string intent = "pi_1",
        CheckoutPurpose purpose = CheckoutPurpose.Deposit, string? sessionId = null) =>
        new("evt_" + intent, sessionId ?? booking.StripeCheckoutSessionId!, booking.Id.Value, purpose, intent, amount,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Quote_prices_the_period_and_estimates_addons()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40); // 16/11/2026, Normal season
        var quote = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue",
            new QuoteRequest(PeriodType.Midweek, start, 9, [new("outboard-motor", 1), new("welcome-hamper", 1)]));

        Assert.Equal(4060m, quote.HirePrice);
        Assert.Equal("Normal", quote.SeasonName);
        Assert.Equal(start.AddDays(4), quote.EndDate);
        Assert.Equal(1000m, quote.AmountDueAtCheckout);
        Assert.False(quote.FullPaymentAtCheckout);
        Assert.Equal(PaymentSchedule.Standard, quote.PaymentSchedule);
        Assert.Equal(95m, quote.Addons.Single(a => a.AddonId == "outboard-motor").EstimatedTotal);
        Assert.Null(quote.Addons.Single(a => a.AddonId == "welcome-hamper").UnitPrice);
        Assert.NotNull(quote.RoomingWarningText);
        Assert.Equal([1000m, 3060m], quote.Milestones.Select(m => m.Amount));
    }

    [Fact]
    public async Task Quote_charges_the_full_price_when_the_balance_is_already_due()
    {
        var app = await TestApp.CreateAsync(now: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)); // Fri 02/10
        // Today + 30 = Sun 01/11; Mon 02/11 start → balance due 03/10, one day after today → deposit.
        var deposit = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Midweek, new DateOnly(2026, 11, 2), 2, []));
        Assert.Equal(1000m, deposit.AmountDueAtCheckout);

        app.Clock.Advance(TimeSpan.FromDays(1)); // Sat 03/10: balance for 02/11 due today
        var full = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Week, new DateOnly(2026, 11, 6), 2, []));
        Assert.Equal(1000m, full.AmountDueAtCheckout); // 06/11: due 07/10, not yet

        app.Clock.UtcNow = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero); // Tue 06/10, today+30 = 05/11
        var week = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Week, new DateOnly(2026, 11, 6), 2, []));
        Assert.Equal(1000m, week.AmountDueAtCheckout);
        app.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero); // Wed 07/10, balance for 06/11 due today
        var dueToday = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Week, new DateOnly(2026, 11, 6), 2, []));
        Assert.Equal(5850m, dueToday.AmountDueAtCheckout);
        Assert.True(dueToday.FullPaymentAtCheckout);
    }

    [Fact]
    public async Task Creating_a_booking_holds_the_dates_and_opens_a_deposit_checkout()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(start), "1.2.3.4");

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(app.Clock.UtcNow + TimeSpan.FromMinutes(40), booking.HoldExpiresAt);
        Assert.Equal(result.SessionId, booking.StripeCheckoutSessionId);
        Assert.Equal(4060m, booking.HirePrice);
        Assert.Equal(2, booking.AddonLines.Count);
        Assert.All(booking.AddonLines, l => Assert.Equal(AddonLineStatus.Requested, l.Status));
        Assert.Equal(4060m, booking.TotalPrice);
        Assert.True(booking.GroupRestrictionDeclaredNotApplicable);
        Assert.NotNull(booking.TermsAcceptedAt);

        var checkout = Assert.Single(app.Gateway.Requests);
        Assert.Equal(1000m, checkout.Amount);
        Assert.Equal(CheckoutPurpose.Deposit, checkout.Purpose);
        Assert.True(checkout.ExpiresAt >= app.Clock.UtcNow.AddMinutes(30));
        Assert.True(checkout.ExpiresAt < booking.HoldExpiresAt);
        Assert.Empty(app.Emails.Sent); // no email while PendingPayment

        var availability = await app.Get<OnlineBookingService>().GetAvailabilityAsync("pacific-blue", start.AddDays(-7), start.AddDays(30));
        Assert.Contains(new DateRangeDto(start, start.AddDays(4)), availability.Unavailable);
    }

    [Fact]
    public async Task Turnstile_failure_is_rejected_before_anything_is_written()
    {
        var app = await TestApp.CreateAsync();
        app.Turnstile.Accept = false;
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null));
        Assert.True(ex.Errors.ContainsKey("turnstileToken"));
        Assert.Empty(app.Store.Bookings.All());
        Assert.Empty(app.Store.Customers.All());
    }

    [Fact]
    public async Task Taken_dates_are_rejected_with_a_conflict()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        await app.Get<OnlineBookingService>().CreateAsync(Request(start), null);
        var ex = await Assert.ThrowsAsync<AvailabilityConflictException>(() =>
            app.Get<OnlineBookingService>().CreateAsync(Request(start, email: "other@example.com"), null));
        Assert.Equal(AvailabilityConflictException.DatesNoLongerAvailable, ex.Message);
    }

    [Fact]
    public async Task Expired_holds_release_the_dates()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        await app.Get<OnlineBookingService>().CreateAsync(Request(start), null);
        app.Clock.Advance(TimeSpan.FromMinutes(41));
        var second = await app.Get<OnlineBookingService>().CreateAsync(Request(start, email: "other@example.com"), null);
        Assert.Equal(BookingStatus.PendingPayment, (await app.BookingAsync(second.Reference)).Status);
    }

    [Fact]
    public async Task Stripe_failure_releases_the_hold()
    {
        var app = await TestApp.CreateAsync();
        app.Gateway.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null));
        Assert.Equal(BookingStatus.Expired, Assert.Single(app.Store.Bookings.All()).Status);
    }

    [Fact]
    public async Task Rooming_warning_text_is_stored_with_the_booking()
    {
        var app = await TestApp.CreateAsync();
        var text = "This boat has 8 beds (4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area). " +
                   "With 10 guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout.";
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40), guests: 10, roomingText: text), null);
        Assert.Equal(text, (await app.BookingAsync(result.Reference)).RoomingWarningAcceptance!.WarningTextShown);
    }

    [Fact]
    public async Task Returning_customer_is_matched_by_email_and_updated()
    {
        var app = await TestApp.CreateAsync();
        await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(50), email: " JANE@example.com ") with { FullName = "Jane Smith" }, null);
        var customer = Assert.Single(app.Store.Customers.All());
        Assert.Equal("Jane Smith", customer.FullName);
    }

    [Fact]
    public async Task Deposit_payment_activates_and_sends_confirmation_and_staff_alert()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var hold = await app.BookingAsync(result.Reference);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(PaymentStatus.DepositPaid, booking.PaymentStatus);
        Assert.Single(app.Emails.Sent, m => m.To == "jane@example.com" && m.Subject.Contains("confirmed"));
        Assert.Single(app.Emails.Sent, m => m.To == "staff@example.test" && m.Subject.Contains("New online booking"));
        Assert.Equal(2, app.Emails.Sent.Count);

        var confirmation = app.Emails.Sent.First();
        Assert.Contains("Outboard motor — $95.00 (subject to confirmation)", confirmation.HtmlBody);
        Assert.Contains("Welcome hamper — price to be confirmed (subject to confirmation)", confirmation.HtmlBody);
        Assert.Contains("$2,000.00 security bond", confirmation.HtmlBody);

        var status = await app.Get<OnlineBookingService>().GetStatusAsync(result.Reference, result.SessionId);
        Assert.Equal(BookingStatus.Active, status.Status);
        await Assert.ThrowsAsync<NotFoundException>(() => app.Get<OnlineBookingService>().GetStatusAsync(result.Reference, "cs_wrong"));
    }

    [Fact]
    public async Task Webhook_replay_is_idempotent()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var hold = await app.BookingAsync(result.Reference);
        var evt = Paid(hold, 1000m);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);

        var booking = await app.BookingAsync(result.Reference);
        Assert.Single(booking.Payments);
        Assert.Equal(2, app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Webhook_retry_after_email_failure_sends_the_missing_emails_once()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var evt = Paid(await app.BookingAsync(result.Reference), 1000m);

        app.Emails.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt));
        Assert.Equal(BookingStatus.Active, (await app.BookingAsync(result.Reference)).Status);

        app.Emails.Fail = false;
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);
        Assert.Equal(2, app.Emails.Sent.Count);
        Assert.Single((await app.BookingAsync(result.Reference)).Payments);
    }

    [Fact]
    public async Task Paid_after_the_hold_expired_and_the_dates_were_taken_keeps_the_payment_and_stays_expired()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var first = await app.Get<OnlineBookingService>().CreateAsync(Request(start), null);
        var firstHold = await app.BookingAsync(first.Reference);

        app.Clock.Advance(TimeSpan.FromMinutes(45));
        await app.Get<BergerHarbour.Application.Notifications.NotificationJob>().RunAsync();
        Assert.Equal(BookingStatus.Expired, (await app.BookingAsync(first.Reference)).Status);

        var second = await app.Get<OnlineBookingService>().CreateAsync(Request(start, email: "other@example.com"), null);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(await app.BookingAsync(second.Reference), 1000m, "pi_2"));
        app.Emails.Sent.Clear();

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(firstHold, 1000m, "pi_1"));

        var late = await app.BookingAsync(first.Reference);
        Assert.Equal(BookingStatus.Expired, late.Status);
        Assert.Equal(1000m, late.AmountPaid);
        Assert.Empty(app.Emails.Sent);
    }

    [Fact]
    public async Task Paid_after_the_hold_expired_but_dates_still_free_activates()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var hold = await app.BookingAsync(result.Reference);
        app.Clock.Advance(TimeSpan.FromMinutes(45));
        await app.Get<BergerHarbour.Application.Notifications.NotificationJob>().RunAsync();

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));

        Assert.Equal(BookingStatus.Active, (await app.BookingAsync(result.Reference)).Status);
        Assert.Equal(2, app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Checkout_session_expired_expires_the_hold()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var hold = await app.BookingAsync(result.Reference);

        await app.Get<StripeWebhookHandler>().HandleExpiredAsync(new CheckoutSessionExpired("evt", "cs_other", hold.Id.Value));
        Assert.Equal(BookingStatus.PendingPayment, (await app.BookingAsync(result.Reference)).Status);

        await app.Get<StripeWebhookHandler>().HandleExpiredAsync(new CheckoutSessionExpired("evt", result.SessionId, hold.Id.Value));
        Assert.Equal(BookingStatus.Expired, (await app.BookingAsync(result.Reference)).Status);
    }

    [Fact]
    public async Task Online_activation_supersedes_overlapping_standbys_and_alerts_staff()
    {
        var app = await TestApp.CreateAsync(online: false);
        var start = app.MondayAfter(40);
        var standby = await app.Get<AdminBookingService>().CreateAsync(new AdminBookingRequest("pacific-blue",
            PeriodType.Custom, start.AddDays(1), start.AddDays(3), 2, "credit@example.com", "Credit Customer", null, true,
            "Store credit", 4000m));
        Assert.Empty(app.Emails.Sent); // stand-bys send nothing

        // The internal strategy is registered in this app, so create the hold through the domain directly.
        var hold = await CreateHoldDirectly(app, start);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));

        var superseded = await app.BookingAsync(standby.Reference);
        Assert.Equal(BookingStatus.Superseded, superseded.Status);
        Assert.Single(app.Emails.Sent, m => m.Subject.Contains("Stand-by booking") && m.To == "staff@example.test");
    }

    private static async Task<Booking> CreateHoldDirectly(TestApp app, DateOnly start)
    {
        var customer = await app.Get<BergerHarbour.Application.Customers.CustomerService>().UpsertAsync("Jane", "jane@example.com", "0400");
        var id = BookingId.New();
        var booking = Booking.CreateOnlineHold(BookingReference.Create(2026, 9000), customer.Id, TestApp.PacificBlue,
            PeriodType.Midweek, new Stay(start, start.AddDays(4)), 4, 4060m, 1000m, PaymentSchedule.Standard, [],
            app.Clock.UtcNow.AddMinutes(40), null, app.Clock.UtcNow, app.Get<PaymentLinkTokens>().HashFor(id), app.Clock.UtcNow, id);
        booking.AttachCheckoutSession("cs_direct", app.Clock.UtcNow);
        await app.Get<IBookingRepository>().SaveAsync(booking);
        return booking;
    }

    [Fact]
    public async Task Payment_link_checkout_charges_the_amount_due_and_records_the_payment()
    {
        var app = await TestApp.CreateAsync();
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(app.MondayAfter(40)), null);
        var hold = await app.BookingAsync(result.Reference);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));
        app.Emails.Sent.Clear();

        var token = app.Get<PaymentLinkTokens>().TokenFor(hold.Id);
        var page = await app.Get<PaymentLinkService>().GetAsync(token);
        Assert.Equal(3060m, page.AmountDueNow);
        Assert.Equal(3060m, page.AmountOwing);

        var checkout = await app.Get<PaymentLinkService>().CreateCheckoutAsync(token);
        Assert.Equal(3060m, checkout.Amount);
        Assert.Equal(CheckoutPurpose.Payment, app.Gateway.Requests[^1].Purpose);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 3060m, "pi_balance", CheckoutPurpose.Payment, checkout.SessionId));
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 3060m, "pi_balance", CheckoutPurpose.Payment, checkout.SessionId));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(PaymentStatus.FullyPaid, booking.PaymentStatus);
        Assert.Single(app.Emails.Sent, m => m.Subject.Contains("Payment received"));

        await Assert.ThrowsAsync<RequestValidationException>(() => app.Get<PaymentLinkService>().CreateCheckoutAsync(token));
        Assert.False((await app.Get<PaymentLinkService>().GetAsync(token)).CanPay);
        await Assert.ThrowsAsync<NotFoundException>(() => app.Get<PaymentLinkService>().GetAsync("not-a-token"));
    }
}
