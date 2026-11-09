using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Customers;
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
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", customerComments: "Prefers the top deck", address: " 1 River Rd "));
        var b = saved.Booking;
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal("SMITH-2701150", b.Reference);
        Assert.Equal(BookingCreator.SystemUser, b.CreatedBy);
        Assert.Equal("Prefers the top deck", b.Customer!.Comments);
        Assert.Equal("1 River Rd", b.Customer.Address);
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
            CustomerComments = "Bring the dog", Address = "2 Wharf St",
        }));
        Assert.Equal("Bring the dog", updated.Booking.Customer!.Comments);
        Assert.Equal("2 Wharf St", h.Db.Customers.All.Single().Address);
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
}
