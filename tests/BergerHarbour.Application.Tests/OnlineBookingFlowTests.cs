using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class OnlineBookingFlowTests
{
    private const string Rooming10 =
        "This boat has 6 adult beds and 2 children's beds (4 queen bedrooms, bunk area (2 singles), single in lounge, " +
        "single in dining area). With 10 guests, some guests will need to share beds. Please make sure your group's " +
        "sleeping arrangements suit this layout.";

    private static CreateOnlineBookingRequest Request(string draftId, DateOnly start, int adults = 4, int children = 2,
        string email = "jane@example.com", string? roomingText = null, IReadOnlyList<BookingAddonRequest>? addons = null) =>
        new(draftId, "pacific-blue", PeriodType.Midweek, start, adults, children, "Jane Citizen", email, "0412 345 678",
            addons ?? [new BookingAddonRequest("outboard-motor", 1), new BookingAddonRequest("welcome-hamper", 1)],
            roomingText is not null, roomingText, true);

    private static async Task<CreateOnlineBookingResult> BookAsync(TestApp app, DateOnly start, string email = "jane@example.com",
        string name = "Jane Citizen", int adults = 4, int children = 2, string? roomingText = null,
        IReadOnlyList<BookingAddonRequest>? addons = null)
    {
        var draftId = await app.DraftAsync(start, email, name);
        return await app.Get<OnlineBookingService>().CreateAsync(
            Request(draftId, start, adults, children, email, roomingText, addons) with { FullName = name });
    }

    private static CheckoutSessionCompleted Paid(Booking booking, decimal amount, string intent = "pi_1",
        CheckoutPurpose purpose = CheckoutPurpose.Deposit, string? sessionId = null) =>
        new("evt_" + intent, sessionId ?? booking.StripeCheckoutSessionId!, booking.Id.Value, purpose, intent, amount,
            DateTimeOffset.UtcNow);

    // ---- Quote --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Quote_prices_the_period_and_add_ons()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40); // 16/11/2026, Normal season, daylight saving
        var quote = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue",
            new QuoteRequest(PeriodType.Midweek, start, 7, 2, [new("outboard-motor", 1), new("welcome-hamper", 1)]));

        Assert.Equal(4060m, quote.HirePrice);
        Assert.Equal("Normal", quote.SeasonName);
        Assert.Equal(start.AddDays(4), quote.EndDate);
        Assert.Equal(("11am", "2pm", "8am", "AEDT"), (quote.CheckInTime, quote.CheckInWindowEnd, quote.CheckOutTime, quote.TimeZoneLabel));
        Assert.Equal((7, 2), (quote.NumberOfAdults, quote.NumberOfChildren));
        Assert.Equal(1000m, quote.AmountDueAtCheckout);
        Assert.Equal(PaymentSchedule.Standard, quote.PaymentSchedule);
        Assert.Equal(95m, quote.Addons.Single(a => a.AddonId == "outboard-motor").LineTotal);
        Assert.Null(quote.Addons.Single(a => a.AddonId == "welcome-hamper").UnitPrice);
        Assert.Equal(4155m, quote.TotalPrice); // priced add-ons count straight away
        Assert.NotNull(quote.RoomingWarningText); // 9 guests > 6 adult + 2 kids beds
        Assert.Equal([1000m, 3155m], quote.Milestones.Select(m => m.Amount));
    }

    [Fact]
    public async Task Quote_lists_add_ons_in_the_admin_defined_order()
    {
        var app = await TestApp.CreateAsync();
        var quote = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue",
            new QuoteRequest(PeriodType.Midweek, app.MondayAfter(40), 2, 0, [new("outboard-motor", 1), new("ice", 2)]));
        Assert.Equal(["ice", "outboard-motor"], quote.Addons.Select(a => a.AddonId));
    }

    [Fact]
    public async Task Checkout_charges_only_the_deposit_even_when_the_balance_is_already_due()
    {
        var app = await TestApp.CreateAsync(now: new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)); // Wed 07/10 11am
        // Week Fri 06/11: the balance fell due 07/10 at 11am — now.
        var quote = await app.Get<OnlineBookingService>().QuoteAsync("pacific-blue",
            new QuoteRequest(PeriodType.Week, new DateOnly(2026, 11, 6), 2, 0, []));
        Assert.Equal(1000m, quote.AmountDueAtCheckout);
        var balance = quote.Milestones.Single(m => m.Kind == MilestoneKind.Balance);
        Assert.Equal(4850m, balance.Amount);
        Assert.True(balance.DueNow);
    }

    [Fact]
    public async Task A_boat_with_its_own_deposit_charges_that_deposit()
    {
        var app = await TestApp.CreateAsync();
        var boats = app.Get<IBoatRepository>();
        var boat = (await boats.GetAsync(TestApp.PacificBlue))!;
        boat.UpdateDetails(boat.Name, boat.Slug, boat.MaxNoOfGuests, boat.AdultBeds, boat.KidsBeds, boat.BeddingDescription,
            boat.SecurityBond, 1500m, app.Clock.UtcNow);
        await boats.SaveAsync(boat);

        Assert.Equal(1500m, (await app.Get<OnlineBookingService>().GetBoatAsync("pacific-blue")).DepositAmount);
        var result = await BookAsync(app, app.MondayAfter(40));
        Assert.Equal(1500m, result.Amount);
        Assert.Equal(1500m, (await app.BookingAsync(result.Reference)).DepositAmount);
    }

    [Fact]
    public async Task Boat_info_has_beds_check_in_window_and_ordered_add_ons()
    {
        var app = await TestApp.CreateAsync();
        var boat = await app.Get<OnlineBookingService>().GetBoatAsync("pacific-blue");
        Assert.Equal((6, 2), (boat.AdultBeds, boat.KidsBeds));
        Assert.Equal(1000m, boat.DepositAmount);
        Assert.Equal(("11am", "2pm", "8am"), (boat.Settings.CheckInTime, boat.Settings.CheckInWindowEnd, boat.Settings.CheckOutTime));
        Assert.Equal(["yabby-pumps", "ice", "bait", "pizza-oven", "fishing-gear", "car-parking", "grocery-service",
            "on-mooring-jetty-stays", "outdoor-heater", "outboard-motor", "welcome-hamper", "skipper"], boat.Addons.Select(a => a.Id));
        var parking = boat.Addons.Single(a => a.Id == "car-parking");
        Assert.True(parking.HasCapacity);
        Assert.Equal("car spots", parking.CapacityUnitLabel);
    }

    // ---- Drafts -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Turnstile_is_checked_when_the_draft_is_created_and_nothing_is_written_on_failure()
    {
        var app = await TestApp.CreateAsync();
        app.Turnstile.Accept = false;
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => app.DraftAsync(app.MondayAfter(40)));
        Assert.True(ex.Errors.ContainsKey("turnstileToken"));
        Assert.Empty(app.Store.BookingDrafts.All());
        Assert.Empty(app.Store.Customers.All());
    }

    [Fact]
    public async Task Drafts_never_create_customers_and_record_progress()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);
        app.Clock.Advance(TimeSpan.FromMinutes(3));
        await app.Get<OnlineBookingService>().UpdateDraftAsync(draftId, new UpdateBookingDraftRequest(PeriodType.Midweek, start,
            "Jane Citizen", "jane@example.com", "+61 412 345 678", 4, 2, [new("ice", 2)], BookingStep.Addons));

        Assert.Empty(app.Store.Customers.All());
        var draft = BookingDraft.FromSnapshot(Assert.Single(app.Store.BookingDrafts.All()));
        Assert.Equal(BookingStep.Addons, draft.LastStepReached);
        Assert.Equal(app.Clock.UtcNow, draft.LastActivityAt);
        Assert.Equal(4, draft.NumberOfAdults);
        Assert.Equal("+61412345678", draft.MobileNumber);
        await Assert.ThrowsAsync<NotFoundException>(() => app.Get<OnlineBookingService>().UpdateDraftAsync("unknown",
            new UpdateBookingDraftRequest(PeriodType.Midweek, start, "Jane", "jane@example.com", "0412 345 678", 1, 0, [], BookingStep.Guests)));
    }

    [Fact]
    public async Task Draft_details_are_validated_including_an_overseas_mobile()
    {
        var app = await TestApp.CreateAsync();
        var service = app.Get<OnlineBookingService>();
        var start = app.MondayAfter(40);
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDraftAsync(
            new CreateBookingDraftRequest("pacific-blue", PeriodType.Midweek, start, "", "bad", "123", "turnstile-ok"), null));
        Assert.Equal(["email", "fullName", "mobile"], ex.Errors.Keys.Order());

        var created = await service.CreateDraftAsync(
            new CreateBookingDraftRequest("pacific-blue", PeriodType.Midweek, start, "Ana", "ana@example.co.nz", "+64 21 123 4567", "ok"), null);
        Assert.NotEmpty(created.DraftId);
    }

    [Fact]
    public async Task A_booking_needs_a_valid_unconverted_draft()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var service = app.Get<OnlineBookingService>();

        var missing = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(Request("", start)));
        Assert.True(missing.Errors.ContainsKey("draftId"));
        var unknown = await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(Request("nope", start)));
        Assert.True(unknown.Errors.ContainsKey("draftId"));

        var draftId = await app.DraftAsync(start);
        var result = await service.CreateAsync(Request(draftId, start));
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(await app.BookingAsync(result.Reference), 1000m));
        var converted = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(Request(draftId, app.MondayAfter(60))));
        Assert.True(converted.Errors.ContainsKey("draftId"));
        Assert.DoesNotContain(app.Store.Bookings.All(), b => b.StartDate == app.MondayAfter(60));
    }

    // ---- Holds --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_booking_holds_the_dates_without_creating_a_customer()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var draftId = await app.DraftAsync(start);
        var result = await app.Get<OnlineBookingService>().CreateAsync(Request(draftId, start));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(app.Clock.UtcNow + TimeSpan.FromMinutes(40), booking.HoldExpiresAt);
        Assert.Equal(result.SessionId, booking.StripeCheckoutSessionId);
        Assert.Equal(4060m, booking.HirePrice);
        Assert.Equal(2, booking.AddonLines.Count);
        Assert.Equal(4155m, booking.TotalPrice);
        Assert.Equal((4, 2), (booking.NumberOfAdults, booking.NumberOfChildren));
        Assert.Equal((new TimeOnly(11, 0), new TimeOnly(8, 0)), (booking.CheckInTime, booking.CheckOutTime));
        Assert.NotNull(booking.TermsAcceptedAt);
        Assert.Null(booking.CustomerId);
        Assert.Equal(("Jane Citizen", "jane@example.com", "+61412345678"),
            (booking.CustomerDetails!.FullName, booking.CustomerDetails.Email, booking.CustomerDetails.MobileNumber));
        Assert.Equal(draftId, booking.BookingDraftId!.Value.Value);
        Assert.Empty(app.Store.Customers.All());
        Assert.Equal(BookingStep.Payment, BookingDraft.FromSnapshot(Assert.Single(app.Store.BookingDrafts.All())).LastStepReached);

        var checkout = Assert.Single(app.Gateway.Requests);
        Assert.Equal(1000m, checkout.Amount);
        Assert.Equal("jane@example.com", checkout.CustomerEmail);
        Assert.Equal(CheckoutPurpose.Deposit, checkout.Purpose);
        Assert.True(checkout.ExpiresAt >= app.Clock.UtcNow.AddMinutes(30));
        Assert.True(checkout.ExpiresAt < booking.HoldExpiresAt);
        Assert.Empty(app.Emails.Sent); // no email while PendingPayment

        var availability = await app.Get<OnlineBookingService>().GetAvailabilityAsync("pacific-blue", start.AddDays(-7), start.AddDays(30));
        Assert.Contains(new DateRangeDto(start, start.AddDays(4)), availability.Unavailable);
    }

    [Fact]
    public async Task Adults_and_children_must_fit_the_boat()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var tooMany = await Assert.ThrowsAsync<RequestValidationException>(() => BookAsync(app, start, adults: 10, children: 3));
        Assert.True(tooMany.Errors.ContainsKey("numberOfChildren"));
        var noAdult = await Assert.ThrowsAsync<RequestValidationException>(() => BookAsync(app, start, adults: 0, children: 2));
        Assert.True(noAdult.Errors.ContainsKey("numberOfAdults"));
    }

    [Fact]
    public async Task Taken_dates_are_rejected_with_a_conflict()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        await BookAsync(app, start);
        var ex = await Assert.ThrowsAsync<AvailabilityConflictException>(() => BookAsync(app, start, "other@example.com"));
        Assert.Equal(AvailabilityConflictException.DatesNoLongerAvailable, ex.Message);
    }

    [Fact]
    public async Task Expired_holds_release_the_dates_and_never_create_a_customer()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        await BookAsync(app, start);
        app.Clock.Advance(TimeSpan.FromMinutes(41));
        await app.Get<NotificationJob>().RunAsync();
        var second = await BookAsync(app, start, "other@example.com");
        Assert.Equal(BookingStatus.PendingPayment, (await app.BookingAsync(second.Reference)).Status);
        Assert.Empty(app.Store.Customers.All());
    }

    [Fact]
    public async Task Stripe_failure_releases_the_hold()
    {
        var app = await TestApp.CreateAsync();
        app.Gateway.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => BookAsync(app, app.MondayAfter(40)));
        Assert.Equal(BookingStatus.Expired, Assert.Single(app.Store.Bookings.All()).Status);
    }

    [Fact]
    public async Task Rooming_warning_text_is_stored_with_the_booking()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40), adults: 8, children: 2, roomingText: Rooming10);
        Assert.Equal(Rooming10, (await app.BookingAsync(result.Reference)).RoomingWarningAcceptance!.WarningTextShown);
    }

    // ---- Add-on capacity ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Car_parking_capacity_is_shared_across_boats_and_a_hard_limit_online()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var stay = new Stay(start, start.AddDays(4));
        await SeedParkingAsync(app, stay, 10);

        var availability = await app.Get<OnlineBookingService>().GetAddonAvailabilityAsync("car-parking", stay.StartDate, stay.EndDate);
        Assert.Equal(1, availability.Remaining);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            app.Get<OnlineBookingService>().GetAddonAvailabilityAsync("ice", stay.StartDate, stay.EndDate));

        var quoteError = await Assert.ThrowsAsync<RequestValidationException>(() => app.Get<OnlineBookingService>().QuoteAsync(
            "pacific-blue", new QuoteRequest(PeriodType.Midweek, start, 2, 0, [new("car-parking", 2)])));
        Assert.Contains("Only 1 car spots remain for your dates.", quoteError.Errors["addons"]);

        var bookError = await Assert.ThrowsAsync<RequestValidationException>(() =>
            BookAsync(app, start, addons: [new BookingAddonRequest("car-parking", 2)]));
        Assert.True(bookError.Errors.ContainsKey("addons"));

        var ok = await BookAsync(app, start, addons: [new BookingAddonRequest("car-parking", 1)]);
        Assert.Equal(1, (await app.BookingAsync(ok.Reference)).QuantityOf(new AddonId("car-parking")));
        Assert.Equal(0, (await app.Get<OnlineBookingService>().GetAddonAvailabilityAsync("car-parking", stay.StartDate, stay.EndDate)).Remaining);
    }

    /// <summary>Another boat's booking using car spots (holds on another boat would do the same).</summary>
    private static async Task SeedParkingAsync(TestApp app, Stay stay, int spots)
    {
        var id = BookingId.New();
        var other = Booking.CreateByStaff(BookingReference.Create(2026, 8000 + spots), CustomerId.New(), new BoatId("kalinda"),
            PeriodType.Custom, stay, 2, 0, Stay.CheckInTime, Stay.CheckOutTime, false, null, 3000m, 1000m,
            PaymentSchedule.Standard, app.Get<PaymentLinkTokens>().HashFor(id), app.Clock.UtcNow, id);
        other.AddAddon(new RequestedAddon(new AddonId("car-parking"), "Car parking", spots, null), app.Clock.UtcNow);
        await app.Get<IBookingRepository>().SaveAsync(other);
    }

    // ---- Deposit webhook ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Deposit_payment_activates_creates_the_customer_converts_the_draft_and_sends_emails()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
        var hold = await app.BookingAsync(result.Reference);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(PaymentStatus.DepositPaid, booking.PaymentStatus);
        var customer = Assert.Single(app.Store.Customers.All());
        Assert.Equal(customer.Id, booking.CustomerId!.Value.Value);
        Assert.Equal("+61412345678", customer.MobileNumber);
        Assert.Equal(booking.Id.Value, Assert.Single(app.Store.BookingDrafts.All()).ConvertedBookingId);

        Assert.Single(app.Emails.Sent, m => m.To == "jane@example.com" && m.Subject.Contains("confirmed"));
        Assert.Single(app.Emails.Sent, m => m.To == "staff@example.test" && m.Subject.Contains("New online booking"));
        Assert.Equal(2, app.Emails.Sent.Count);

        var confirmation = app.Emails.Sent.First();
        Assert.Contains("Outboard motor — $95.00", confirmation.HtmlBody);
        Assert.Contains("Welcome hamper — price on request", confirmation.HtmlBody);
        Assert.Contains("$2,000.00 security bond", confirmation.HtmlBody);
        Assert.Contains("11am – 2pm AEDT", confirmation.HtmlBody);
        Assert.Contains("4 adults, 2 children", confirmation.HtmlBody);
        Assert.DoesNotContain("is now due", confirmation.HtmlBody);

        var status = await app.Get<OnlineBookingService>().GetStatusAsync(result.Reference, result.SessionId);
        Assert.Equal(BookingStatus.Active, status.Status);
        Assert.Equal("jane@example.com", status.Email);
        await Assert.ThrowsAsync<NotFoundException>(() => app.Get<OnlineBookingService>().GetStatusAsync(result.Reference, "cs_wrong"));
    }

    [Fact]
    public async Task Returning_customer_is_matched_by_email_and_updated_when_the_deposit_is_paid()
    {
        var app = await TestApp.CreateAsync();
        var first = await BookAsync(app, app.MondayAfter(40));
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(await app.BookingAsync(first.Reference), 1000m));
        var second = await BookAsync(app, app.MondayAfter(50), email: " JANE@example.com ", name: "Jane Smith");
        Assert.Equal("Jane Citizen", Assert.Single(app.Store.Customers.All()).FullName); // not before payment

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(await app.BookingAsync(second.Reference), 1000m, "pi_2"));
        Assert.Equal("Jane Smith", Assert.Single(app.Store.Customers.All()).FullName);
    }

    [Fact]
    public async Task Webhook_replay_is_idempotent()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
        var evt = Paid(await app.BookingAsync(result.Reference), 1000m);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(evt);

        Assert.Single((await app.BookingAsync(result.Reference)).Payments);
        Assert.Single(app.Store.Customers.All());
        Assert.Equal(2, app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Webhook_retry_after_email_failure_sends_the_missing_emails_once()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
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
    public async Task Paid_after_the_hold_expired_and_the_dates_were_taken_keeps_the_payment_and_creates_no_customer()
    {
        var app = await TestApp.CreateAsync();
        var start = app.MondayAfter(40);
        var first = await BookAsync(app, start);
        var firstHold = await app.BookingAsync(first.Reference);

        app.Clock.Advance(TimeSpan.FromMinutes(45));
        await app.Get<NotificationJob>().RunAsync();
        Assert.Equal(BookingStatus.Expired, (await app.BookingAsync(first.Reference)).Status);

        var second = await BookAsync(app, start, "other@example.com");
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(await app.BookingAsync(second.Reference), 1000m, "pi_2"));
        app.Emails.Sent.Clear();

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(firstHold, 1000m, "pi_1"));

        var late = await app.BookingAsync(first.Reference);
        Assert.Equal(BookingStatus.Expired, late.Status);
        Assert.Equal(1000m, late.AmountPaid);
        Assert.Null(late.CustomerId);
        Assert.Equal("other@example.com", Assert.Single(app.Store.Customers.All()).Email);
        Assert.Empty(app.Emails.Sent);
    }

    [Fact]
    public async Task Paid_after_the_hold_expired_but_dates_still_free_activates()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
        var hold = await app.BookingAsync(result.Reference);
        app.Clock.Advance(TimeSpan.FromMinutes(45));
        await app.Get<NotificationJob>().RunAsync();

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.NotNull(booking.CustomerId);
        Assert.Equal(2, app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Checkout_session_expired_expires_the_hold()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
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
        var standby = await app.Get<AdminBookingService>().CreateAsync(AdminBookingTests.Request(start.AddDays(1), 2,
            standby: true, email: "credit@example.com") with { Comments = "Store credit" });
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
        var id = BookingId.New();
        var booking = Booking.CreateOnlineHold(BookingReference.Create(2026, 9000),
            new BookingCustomerDetails("Jane", "jane@example.com", "0412 345 678"), BookingDraftId.New(), TestApp.PacificBlue,
            PeriodType.Midweek, new Stay(start, start.AddDays(4)), 4, 0, 4060m, 1000m, PaymentSchedule.Standard, [],
            app.Clock.UtcNow.AddMinutes(40), null, app.Clock.UtcNow, app.Get<PaymentLinkTokens>().HashFor(id), app.Clock.UtcNow, id);
        booking.AttachCheckoutSession("cs_direct", app.Clock.UtcNow);
        await app.Get<IBookingRepository>().SaveAsync(booking);
        return booking;
    }

    [Fact]
    public async Task Payment_link_checkout_charges_the_amount_due_and_records_the_payment()
    {
        var app = await TestApp.CreateAsync();
        var result = await BookAsync(app, app.MondayAfter(40));
        var hold = await app.BookingAsync(result.Reference);
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 1000m));
        app.Emails.Sent.Clear();

        var token = app.Get<PaymentLinkTokens>().TokenFor(hold.Id);
        var page = await app.Get<PaymentLinkService>().GetAsync(token);
        Assert.Equal(3155m, page.AmountDueNow);
        Assert.Equal(3155m, page.AmountOwing);

        var checkout = await app.Get<PaymentLinkService>().CreateCheckoutAsync(token);
        Assert.Equal(3155m, checkout.Amount);
        Assert.Equal(CheckoutPurpose.Payment, app.Gateway.Requests[^1].Purpose);

        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 3155m, "pi_balance", CheckoutPurpose.Payment, checkout.SessionId));
        await app.Get<StripeWebhookHandler>().HandleCompletedAsync(Paid(hold, 3155m, "pi_balance", CheckoutPurpose.Payment, checkout.SessionId));

        var booking = await app.BookingAsync(result.Reference);
        Assert.Equal(PaymentStatus.FullyPaid, booking.PaymentStatus);
        Assert.Single(app.Emails.Sent, m => m.Subject.Contains("Payment received"));

        await Assert.ThrowsAsync<RequestValidationException>(() => app.Get<PaymentLinkService>().CreateCheckoutAsync(token));
        Assert.False((await app.Get<PaymentLinkService>().GetAsync(token)).CanPay);
        await Assert.ThrowsAsync<NotFoundException>(() => app.Get<PaymentLinkService>().GetAsync("not-a-token"));
    }
}
