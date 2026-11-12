using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

public class StaffBookingServiceTests
{
    private readonly ApplicationHarness h = new();

    [Fact]
    public async Task Staff_bookings_are_active_with_a_reference_match_or_create_the_customer_and_send_1()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", customerComments: "Prefers the top deck", address: new AddressDto(" 1 River Rd ", null, "Brooklyn", "NSW", "2083", "AU")));
        var b = saved.Booking;
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal("SMITH-2701150", b.Reference);
        Assert.Equal(BookingCreator.SystemUser, b.CreatedBy);
        Assert.Equal("Prefers the top deck", b.Customer!.Comments);
        Assert.Equal(new AddressDto("1 River Rd", null, "Brooklyn", "NSW", "2083", "AU"), b.Customer.Address);
        Assert.Single(h.EmailsTo("john@example.com"));
        h.Activate("misty-blue");
        var second = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", slug: "misty-blue", email: "JOHN@example.com"));
        Assert.Equal("SMITH-2701151", second.Booking.Reference);
        Assert.Single(h.Db.Customers.All);
    }

    [Fact]
    public async Task Standby_bookings_send_no_email()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", standby: true));
        Assert.Empty(h.Emails.Sent);
    }

    [Fact]
    public async Task Overlaps_are_409_naming_the_reference_and_inactive_boats_cannot_be_booked()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var e = await Assert.ThrowsAsync<ConflictException>(() => h.Staff.CreateAsync(h.StaffRequest("2027-01-18", "2027-01-20", email: "x@example.com")));
        Assert.Equal(["SMITH-2701150"], e.ConflictingReferences);
        var standby = await Assert.ThrowsAsync<ConflictException>(() => h.Staff.CreateAsync(h.StaffRequest("2027-01-18", "2027-01-20", standby: true)));
        Assert.NotNull(standby);
        var inactive = await Assert.ThrowsAsync<DomainValidationException>(() => h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", slug: "kalinda")));
        Assert.True(inactive.Errors.Has("boatId"));
    }

    [Fact]
    public async Task Staff_warnings_are_returned_not_errors()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", adults: 13));
        Assert.Contains(saved.Warnings, w => w.Contains("berth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Addon_lines_new_edited_and_left_out_lines_are_saved_with_the_booking()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", lines:
        [
            new StaffAddonLineInput(null, "ice", 2, 10), new StaffAddonLineInput(null, "welcome-hamper", 1, null),
        ]));
        var detail = saved.Booking;
        Assert.Equal(4020m, detail.TotalPrice);
        var ice = detail.AddonLines.Single(l => l.AddonId == "ice");
        var updated = await h.Staff.UpdateAsync(detail.Id, UpdateRequest(detail, r => r with
        {
            AddonLines = [new StaffAddonLineInput(ice.Id, "ice", 3, 12), new StaffAddonLineInput(null, "outboard-motor", 1, 95)],
        }));
        var lines = updated.Booking.AddonLines;
        Assert.Equal(2, lines.Count);
        Assert.Equal((ice.Id, 3, 12m), (lines[0].Id, lines[0].Quantity, lines[0].UnitPrice!.Value));
        Assert.Equal("outboard-motor", lines[1].AddonId);
        Assert.Equal(4000m + 36m + 95m, updated.Booking.TotalPrice);
    }

    [Fact]
    public async Task Quote_prefills_rates_and_addon_prices_null_for_price_on_request_long_weekend_and_custom()
    {
        var boatId = h.Boat("pacific-blue").Id.Value;
        var midweek = await h.Staff.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.Midweek, D("2027-03-08"), ["outboard-motor", "welcome-hamper"]));
        Assert.Equal(4060m, midweek.HirePrice);
        Assert.Equal([95m, null], midweek.AddonPrices.Select(p => p.UnitPrice));
        var longWeekend = await h.Staff.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.LongWeekend, D("2027-03-05"), ["outboard-motor"]));
        Assert.Equal(4620m, longWeekend.HirePrice);
        Assert.Null(longWeekend.AddonPrices.Single().UnitPrice);
        var custom = await h.Staff.QuoteAsync(new AdminQuoteRequest(boatId, PeriodType.Custom, D("2027-03-05"), ["outboard-motor"]));
        Assert.Null(custom.HirePrice);
        Assert.Null(custom.AddonPrices.Single().UnitPrice);
    }

    [Fact]
    public async Task Customer_comments_and_address_are_saved_from_create_and_update()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var updated = await h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with
        {
            CustomerComments = "Bring the dog", Address = new AddressDto(null, null, "Wisemans Ferry", null, null, "AU"),
        }));
        Assert.Equal("Bring the dog", updated.Booking.Customer!.Comments);
        var address = h.Db.Customers.All.Single().Address!;
        Assert.Equal("Wisemans Ferry", address.Locality);
        Assert.Null(address.Line1);
    }

    [Fact]
    public async Task Create_and_update_reject_an_invalid_email_or_mobile()
    {
        var bad = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", email: "jane@gmail")));
        Assert.True(bad.Errors.Has("email"));
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with { Email = "jane@gmail", Mobile = "+61295551234" })));
        Assert.True(e.Errors.Has("email"));
        Assert.True(e.Errors.Has("mobile"));
    }

    [Fact]
    public async Task Version_conflicts_are_rejected()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        await h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking));
        await Assert.ThrowsAsync<ConcurrencyException>(() => h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking)));
    }

    [Fact]
    public async Task Cancel_and_reactivate_subject_to_availability()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var cancelled = await h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with { Status = BookingStatus.Cancelled }));
        Assert.Equal(BookingStatus.Cancelled, cancelled.Booking.Status);
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", email: "other@example.com", lastName: "Jones"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(cancelled.Booking, r => r with { Status = BookingStatus.Active })));
    }

    [Fact]
    public async Task Reference_is_unchanged_when_dates_or_last_name_change()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var updated = await h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with
        {
            StartDate = D("2027-02-01"), EndDate = D("2027-02-05"), LastName = "Jones",
        }));
        Assert.Equal("SMITH-2701150", updated.Booking.Reference);
    }

    [Fact]
    public async Task Manual_payments_and_payment_links()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var paid = await h.Staff.AddManualPaymentAsync(saved.Booking.Id, new ManualPaymentRequest(1000, D("2026-11-09"), "Bank transfer", saved.Booking.Version));
        Assert.Equal(PaymentStatus.DepositPaid, paid.PaymentStatus);
        Assert.Equal(1, h.CountEmails("Payment received"));
        await h.Staff.SendPaymentLinkAsync(saved.Booking.Id);
        await h.Staff.SendPaymentLinkAsync(saved.Booking.Id);
        Assert.Equal(2, h.CountEmails("Payment due"));
        var link = h.Emails.Sent.Last().HtmlBody;
        Assert.Contains("https://bookings.test/pay/?token=", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frequent_floater_is_flagged_on_the_detail_and_the_lookup()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var second = await h.Staff.CreateAsync(h.StaffRequest("2027-06-07", "2027-06-11"));
        Assert.True(second.Booking.FrequentFloater.IsFrequentFloater);
        Assert.Equal(1, second.Booking.FrequentFloater.OtherBookingCount);
        var lookup = await h.Get<CustomerService>().GetByEmailAsync("john@example.com");
        Assert.Equal(2, lookup.FrequentFloater.OtherBookingCount);
    }

    [Fact]
    public async Task Search_matches_names_emails_references_and_online_snapshots()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var draft = await h.CreateDraftAsync(email: "zoe@example.com", lastName: "Zed");
        await h.Online.CreateAsync(BookingRequest(draft, email: "zoe@example.com", lastName: "Zed"));
        Assert.Single(await h.Staff.SearchAsync("smith-2701150"));
        Assert.Single(await h.Staff.SearchAsync("john smith"));
        Assert.Single(await h.Staff.SearchAsync("zoe@"));
    }

    [Fact]
    public async Task A_lapsed_hold_is_reported_as_Expired_everywhere_without_being_saved()
    {
        var draft = await h.CreateDraftAsync(email: "zoe@example.com", lastName: "Zed");
        var created = await h.Online.CreateAsync(BookingRequest(draft, email: "zoe@example.com", lastName: "Zed"));
        Assert.Equal(BookingStatus.PendingPayment, (await h.Staff.GetAsync(created.BookingId)).Status);
        h.Clock.Advance(h.BookingOptions.HoldLifetime);

        var detail = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(BookingStatus.Expired, detail.Status);
        Assert.Null(detail.HoldExpiresAt);
        Assert.Equal(1000m, detail.CheckoutAmount);
        Assert.Equal(BookingStatus.Expired, (await h.Staff.SearchAsync("zoe@")).Single().Status);
        Assert.Empty((await h.Staff.GetTimelineAsync(D("2027-03-01"), D("2027-03-31"), false)).Bookings);
        Assert.Equal(BookingStatus.Expired, (await h.Staff.GetTimelineAsync(D("2027-03-01"), D("2027-03-31"), true)).Bookings.Single().Status);
        Assert.Equal(BookingStatus.PendingPayment, h.Booking(created.BookingId).Status);
    }

    [Fact]
    public async Task Holds_are_read_only_and_take_no_manual_payment_or_payment_link_while_pending()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        var detail = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(h.Booking(created.BookingId).HoldExpiresAt, detail.HoldExpiresAt);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.UpdateAsync(detail.Id, UpdateRequest(detail)));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            h.Staff.AddManualPaymentAsync(detail.Id, new ManualPaymentRequest(1000, D("2026-11-09"), null, detail.Version)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.SendPaymentLinkAsync(detail.Id));

        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired)));
        Assert.Empty(h.Booking(created.BookingId).Payments);
    }

    [Fact]
    public async Task Staff_confirmation_records_the_payment_and_activates_the_booking_sending_only_1()
    {
        var standby = await h.Staff.CreateAsync(h.StaffRequest("2027-03-09", "2027-03-11", standby: true, email: "credit@example.com"));
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromDays(3));
        var expired = await h.Staff.GetAsync(created.BookingId);
        h.Emails.Sent.Clear();

        var confirmed = await h.Staff.AddManualPaymentAsync(expired.Id, new ManualPaymentRequest(1000, D("2026-11-12"), "Phoned in", expired.Version));

        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.Equal("SMITH-2703080", confirmed.Reference);
        Assert.Equal(h.Clock.UtcNow, confirmed.ActivatedAt);
        Assert.Equal(BookingCreator.Customer, confirmed.CreatedBy);
        Assert.Equal(1000m, confirmed.AmountPaid);
        Assert.Equal("jane@example.com", confirmed.Customer!.Email);
        Assert.True(h.Db.Drafts.Get(h.Booking(created.BookingId).BookingDraftId!.Value)!.IsConverted);
        Assert.Equal(BookingStatus.Superseded, h.Booking(standby.Booking.Id).Status);
        Assert.Single(h.EmailsTo("jane@example.com"), m => m.Subject.Contains("is confirmed", StringComparison.Ordinal));
        Assert.Equal(0, h.CountEmails("Payment received"));
        Assert.Equal(0, h.CountEmails("New online booking"));
        Assert.Equal(1, h.CountEmails("Stand-by booking superseded"));
        Assert.Null(h.Db.Customers.All.Single(c => c.Email.Value == "jane@example.com").Address);
    }

    [Fact]
    public async Task Staff_confirmation_needs_at_least_the_checkout_amount_and_records_nothing_otherwise()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.AddManualPaymentAsync(expired.Id, new ManualPaymentRequest(999.99m, D("2026-11-09"), null, expired.Version)));
        Assert.Equal("To confirm this booking, record at least $1,000.00.", e.Errors.ByField["amount"][0]);
        var stored = h.Booking(created.BookingId);
        Assert.Empty(stored.Payments);
        Assert.Null(stored.Reference);
        Assert.Empty(h.References.Counters);
    }

    [Fact]
    public async Task Staff_confirmation_of_taken_dates_is_409_naming_the_conflict_and_records_nothing()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var other = await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        var expired = await h.Staff.GetAsync(created.BookingId);
        var e = await Assert.ThrowsAsync<ConflictException>(() =>
            h.Staff.AddManualPaymentAsync(expired.Id, new ManualPaymentRequest(1000, D("2026-11-09"), null, expired.Version)));
        Assert.Equal([other.Booking.Reference!], e.ConflictingReferences);
        var stored = h.Booking(created.BookingId);
        Assert.Empty(stored.Payments);
        Assert.Null(stored.Reference);
        Assert.Null(stored.CustomerId);
    }

    [Fact]
    public async Task A_paid_but_expired_booking_cannot_be_confirmed_by_staff()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        await h.PayDepositAsync(created.BookingId);
        var paidButExpired = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(BookingStatus.Expired, paidButExpired.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            h.Staff.AddManualPaymentAsync(paidButExpired.Id, new ManualPaymentRequest(1000, D("2026-11-09"), null, paidButExpired.Version)));
        Assert.Single(h.Booking(created.BookingId).Payments);
    }
}
