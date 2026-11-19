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
        h.Deactivate("kalinda");
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
        var paid = (await h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with
        {
            Payments = [NewPayment(1000, "2026-11-09", "Bank transfer")],
        }))).Booking;
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
        Assert.Equal(BookingStatus.Expired, (await h.Staff.SearchAsync("zoe@")).Single().Status);
        Assert.Empty((await h.Staff.GetTimelineAsync(D("2027-03-01"), D("2027-03-31"), false)).Bookings);
        Assert.Equal(BookingStatus.Expired, (await h.Staff.GetTimelineAsync(D("2027-03-01"), D("2027-03-31"), true)).Bookings.Single().Status);
        Assert.Equal(BookingStatus.PendingPayment, h.Booking(created.BookingId).Status);
    }

    [Fact]
    public async Task A_PendingPayment_hold_is_read_only_and_a_staff_save_of_a_lapsed_one_stores_it_as_Expired()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        var detail = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(h.Booking(created.BookingId).HoldExpiresAt, detail.HoldExpiresAt);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.UpdateAsync(detail.Id, UpdateRequest(detail)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.SendPaymentLinkAsync(detail.Id));

        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        Assert.Equal(BookingStatus.PendingPayment, h.Booking(created.BookingId).Status);
        var expired = await h.Staff.GetAsync(created.BookingId);
        var saved = await h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired, r => r with { BookingComments = "Called back" }));
        Assert.Equal(BookingStatus.Expired, saved.Booking.Status);
        Assert.Equal(BookingStatus.Expired, h.Booking(created.BookingId).Status);
        Assert.Equal("Called back", h.Booking(created.BookingId).BookingComments);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Staff.SendPaymentLinkAsync(expired.Id));
    }

    [Fact]
    public async Task Every_field_of_an_Expired_booking_is_saved_and_it_stays_Expired_without_a_reference_customer_or_email()
    {
        h.Activate("misty-blue");
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        Assert.Null(expired.Customer);
        Assert.Null(expired.CustomerDetails!.Address);
        h.Emails.Sent.Clear();

        var saved = (await h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired, r => r with
        {
            BoatId = h.Boat("misty-blue").Id.Value, PeriodType = PeriodType.Custom, StartDate = D("2027-04-05"), EndDate = D("2027-04-08"),
            NumberOfAdults = 3, NumberOfChildren = 1, CheckInTime = "12:00", CheckOutTime = "09:00", IsStandby = true, BookingComments = "Phoned",
            HirePrice = 2500, AddonLines = [new StaffAddonLineInput(null, "ice", 2, 10)], FirstName = "Janet", LastName = "Jones",
            Email = "janet@example.com", Mobile = "0412 000 999", Address = new AddressDto("1 Wharf St", null, "Paradise Point", "QLD", "4216", "AU"),
            CustomerComments = "Likes the top deck",
        }))).Booking;

        Assert.Equal(BookingStatus.Expired, saved.Status);
        Assert.Equal((h.Boat("misty-blue").Id.Value, D("2027-04-05"), D("2027-04-08")), (saved.BoatId, saved.StartDate, saved.EndDate));
        Assert.Equal((3, 1, "12:00", "09:00", true, "Phoned"), (saved.NumberOfAdults, saved.NumberOfChildren, saved.CheckInTime, saved.CheckOutTime,
            saved.IsStandby, saved.BookingComments));
        Assert.Equal(2520m, saved.TotalPrice);
        Assert.Null(saved.Reference);
        Assert.Null(saved.Customer);
        Assert.Equal(new CustomerDetailsDto("Janet", "Jones", "janet@example.com", "+61412000999",
            new AddressDto("1 Wharf St", null, "Paradise Point", "QLD", "4216", "AU"), "Likes the top deck"), saved.CustomerDetails);
        Assert.Empty(h.Db.Customers.All);
        Assert.Empty(h.References.Counters);
        Assert.Empty(h.Emails.Sent);
    }

    [Fact]
    public async Task A_status_change_is_rejected_on_an_Expired_booking()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired, r => r with { Status = BookingStatus.Active })));
        Assert.True(e.Errors.Has("status"));
        Assert.Equal(BookingStatus.PendingPayment, h.Booking(created.BookingId).Status); // lapsed, but nothing was saved
        Assert.Null(h.Booking(created.BookingId).Reference);
    }

    [Fact]
    public async Task Staff_confirmation_saves_the_edits_and_payments_and_activates_the_booking_sending_only_1()
    {
        var standby = await h.Staff.CreateAsync(h.StaffRequest("2027-03-09", "2027-03-11", standby: true, email: "credit@example.com"));
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromDays(3));
        var expired = await h.Staff.GetAsync(created.BookingId);
        h.Emails.Sent.Clear();

        var confirmed = await h.SaveWithPaymentAsync(expired, 1000, "2026-11-12", r => r with
        {
            LastName = "Jones", BookingComments = "Paid by phone", CustomerComments = "Regular", Address = new AddressDto("1 Wharf St", null, null, null, null, "AU"),
        });

        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.Equal("JONES-2703080", confirmed.Reference);
        Assert.Equal(h.Clock.UtcNow, confirmed.ActivatedAt);
        Assert.Equal(BookingCreator.Customer, confirmed.CreatedBy);
        Assert.Equal(1000m, confirmed.AmountPaid);
        Assert.Equal("Paid by phone", confirmed.BookingComments);
        Assert.Equal(("jane@example.com", "Jones", "Regular"), (confirmed.Customer!.Email, confirmed.Customer.LastName, confirmed.Customer.Comments));
        Assert.Equal("1 Wharf St", h.Db.Customers.All.Single(c => c.Email.Value == "jane@example.com").Address!.Line1);
        Assert.True(h.Db.Drafts.Get(h.Booking(created.BookingId).BookingDraftId!.Value)!.IsConverted);
        Assert.Equal(BookingStatus.Superseded, h.Booking(standby.Booking.Id).Status);
        Assert.Single(h.EmailsTo("jane@example.com"), m => m.Subject.Contains("is confirmed", StringComparison.Ordinal));
        Assert.Equal(0, h.CountEmails("Payment received"));
        Assert.Equal(0, h.CountEmails("New online booking"));
        Assert.Equal(1, h.CountEmails("Stand-by booking superseded"));
    }

    [Fact]
    public async Task Staff_confirmation_writes_the_saved_address_and_comments_over_an_existing_customers()
    {
        var existing = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19", email: "jane@example.com", customerComments: "Old note",
            address: new AddressDto("9 Old Rd", null, null, null, null, "AU")));
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);

        var confirmed = await h.SaveWithPaymentAsync(expired, 1000, "2026-11-09", r => r with { CustomerComments = "New note", Address = null });

        Assert.Equal(existing.Booking.Customer!.Id, confirmed.Customer!.Id);
        Assert.Equal("New note", confirmed.Customer.Comments);
        Assert.Null(confirmed.Customer.Address);
        Assert.Single(h.Db.Customers.All);
    }

    [Fact]
    public async Task A_part_payment_on_an_Expired_booking_is_rejected_and_saves_nothing()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.SaveWithPaymentAsync(expired, 999.99m, "2026-11-09", r => r with { BookingComments = "Partial" }));
        Assert.Equal("To activate this booking, record at least $1,000.00.", e.Errors.ByField["payments"][0]);
        var stored = h.Booking(created.BookingId);
        Assert.Empty(stored.Payments);
        Assert.Null(stored.Reference);
        Assert.Null(stored.BookingComments);
        Assert.Empty(h.References.Counters);
    }

    [Fact]
    public async Task An_Expired_booking_moved_onto_taken_dates_is_409_but_edits_that_leave_taken_dates_alone_save()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var other = await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        var later = await h.Staff.CreateAsync(h.StaffRequest("2027-04-05", "2027-04-09", email: "later@example.com", lastName: "Later"));
        var expired = await h.Staff.GetAsync(created.BookingId);

        var moved = await Assert.ThrowsAsync<ConflictException>(() => h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired, r => r with
        {
            StartDate = D("2027-04-06"), EndDate = D("2027-04-08"),
        })));
        Assert.Equal([later.Booking.Reference!], moved.ConflictingReferences);
        Assert.Equal(D("2027-03-08"), h.Booking(created.BookingId).StartDate);

        var edited = await h.Staff.UpdateAsync(expired.Id, UpdateRequest(expired, r => r with { BookingComments = "Dates taken" }));
        Assert.Equal(BookingStatus.Expired, edited.Booking.Status);

        var confirming = await Assert.ThrowsAsync<ConflictException>(() => h.SaveWithPaymentAsync(edited.Booking, 1000, "2026-11-09"));
        Assert.Equal([other.Booking.Reference!], confirming.ConflictingReferences);
        var stored = h.Booking(created.BookingId);
        Assert.Empty(stored.Payments);
        Assert.Null(stored.CustomerId);
        Assert.Equal(BookingStatus.Expired, stored.Status);

        var confirmed = await h.SaveWithPaymentAsync(edited.Booking, 1000, "2026-11-09", r => r with
        {
            StartDate = D("2027-05-03"), EndDate = D("2027-05-07"),
        });
        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.Equal("SMITH-2705030", confirmed.Reference);
    }

    [Fact]
    public async Task A_paid_but_expired_booking_is_confirmed_by_saving_it_on_free_dates_and_stays_Expired_without_its_payment()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        await h.PayDepositAsync(created.BookingId);
        var paidButExpired = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(BookingStatus.Expired, paidButExpired.Status);
        Assert.Equal("SMITH-2703080", paidButExpired.Reference);

        // Saving it where it is would confirm it on taken dates.
        await Assert.ThrowsAsync<ConflictException>(() => h.Staff.UpdateAsync(paidButExpired.Id, UpdateRequest(paidButExpired)));

        var refunded = await h.Staff.UpdateAsync(paidButExpired.Id, UpdateRequest(paidButExpired, r => r with { Payments = [] }));
        Assert.Equal(BookingStatus.Expired, refunded.Booking.Status);
        Assert.Empty(refunded.Booking.Payments);
        Assert.Single(h.Booking(created.BookingId).RemovedStripePaymentIntentIds);
    }

    [Fact]
    public async Task A_paid_but_expired_booking_moved_to_free_dates_is_confirmed_with_its_payment_and_keeps_its_reference()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        await h.PayDepositAsync(created.BookingId);
        var paidButExpired = await h.Staff.GetAsync(created.BookingId);
        h.Emails.Sent.Clear();

        var confirmed = (await h.Staff.UpdateAsync(paidButExpired.Id, UpdateRequest(paidButExpired, r => r with
        {
            StartDate = D("2027-04-05"), EndDate = D("2027-04-09"),
        }))).Booking;

        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.Equal("SMITH-2703080", confirmed.Reference);
        Assert.Equal("jane@example.com", confirmed.Customer!.Email);
        Assert.Single(h.EmailsTo("jane@example.com"), m => m.Subject.Contains("is confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_Expired_stand_by_confirmed_becomes_an_Active_stand_by_and_sends_no_email()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(h.BookingOptions.HoldLifetime);
        var expired = await h.Staff.GetAsync(created.BookingId);
        h.Emails.Sent.Clear();

        var confirmed = await h.SaveWithPaymentAsync(expired, 1000, "2026-11-09", r => r with { IsStandby = true });

        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.True(confirmed.IsStandby);
        Assert.NotNull(confirmed.Reference);
        Assert.Empty(h.Emails.Sent);
    }

    [Fact]
    public async Task Create_and_update_require_a_mobile()
    {
        var create = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19") with { Mobile = null }));
        Assert.Equal("Mobile is required.", create.Errors.ByField["mobile"][0]);
        Assert.Empty(h.Db.Customers.All);

        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var update = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with { Mobile = " " })));
        Assert.Equal("Mobile is required.", update.Errors.ByField["mobile"][0]);
        Assert.Equal("+61412000111", h.Db.Customers.All.Single().MobileNumber.Value);
    }

    [Fact]
    public async Task A_secondary_mobile_belongs_to_its_booking_and_changing_it_leaves_the_customer_and_other_bookings_alone()
    {
        var first = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19") with { SecondaryMobile = "0412 000 222" });
        var second = await h.Staff.CreateAsync(h.StaffRequest("2027-02-15", "2027-02-19"));
        Assert.Equal("+61412000222", first.Booking.SecondaryMobile);
        Assert.Null(second.Booking.SecondaryMobile);

        var updated = await h.Staff.UpdateAsync(second.Booking.Id, UpdateRequest(second.Booking, r => r with { SecondaryMobile = "+61412000333" }));
        Assert.Equal("+61412000333", updated.Booking.SecondaryMobile);
        Assert.Equal("+61412000333", (await h.Staff.GetAsync(second.Booking.Id)).SecondaryMobile);
        Assert.Equal("+61412000222", (await h.Staff.GetAsync(first.Booking.Id)).SecondaryMobile);
        Assert.Single(h.Db.Customers.All);

        var cleared = await h.Staff.UpdateAsync(second.Booking.Id, UpdateRequest(updated.Booking, r => r with { SecondaryMobile = null }));
        Assert.Null(cleared.Booking.SecondaryMobile);
        Assert.Equal("+61412000222", (await h.Staff.GetAsync(first.Booking.Id)).SecondaryMobile);
    }

    [Fact]
    public async Task A_secondary_mobile_equal_to_the_mobile_is_rejected_on_create_and_update()
    {
        var create = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19") with { Mobile = "0412 000 111", SecondaryMobile = "+61412000111" }));
        Assert.Equal("The secondary mobile can't be the same as the mobile.", create.Errors.ByField["secondaryMobile"][0]);
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"));
        var update = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.UpdateAsync(saved.Booking.Id, UpdateRequest(saved.Booking, r => r with { SecondaryMobile = saved.Booking.Customer!.Mobile })));
        Assert.True(update.Errors.Has("secondaryMobile"));
    }
}
