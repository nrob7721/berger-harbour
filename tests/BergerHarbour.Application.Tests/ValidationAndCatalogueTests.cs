using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using static BergerHarbour.Application.Tests.Harness;

namespace BergerHarbour.Application.Tests;

public class ValidationStrategyTests
{
    private readonly Harness _h = new();

    private async Task<BookingValidationResult> Online(BookingRequest r) => await new OnlineBookingValidationStrategy(_h.Db).ValidateAsync(r);
    private async Task<BookingValidationResult> Internal(BookingRequest r) => await new InternalBookingValidationStrategy(_h.Db).ValidateAsync(r);

    private async Task<BookingRequest> Request(string start = "2027-03-08", PeriodType type = PeriodType.Midweek, int adults = 2, int children = 0,
        IReadOnlyList<AddonQuantity>? addons = null, ValidationScope scope = ValidationScope.Quote, DateOnly? end = null, string slug = "pacific-blue") =>
        new()
        {
            Boat = _h.Boat(slug), PeriodType = type, StartDate = D(start), EndDate = end, NumberOfAdults = adults, NumberOfChildren = children,
            Addons = addons ?? [], Reference = await _h.Get<ReferenceDataLoader>().LoadAsync(), Now = _h.Clock.UtcNow, Scope = scope,
            Contact = new ContactInput("John", "Smith", "john@example.com", "0412345678"),
        };

    [Fact]
    public async Task Lead_time_boundary_is_today_plus_minimum_days()
    {
        // Today 09/11/2026 + 30 days = 09/12/2026 (a Wednesday). Week stays may start on a Friday: 11/12 is fine, 04/12 is too soon.
        Assert.True((await Online(await Request("2026-12-11", PeriodType.Weekend))).IsValid);
        var tooSoon = await Online(await Request("2026-12-04", PeriodType.Weekend));
        Assert.Contains("startDate", tooSoon.Errors.Keys);
        _h.Clock.UtcNow = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero); // today 11/11 → earliest 11/12
        Assert.True((await Online(await Request("2026-12-11", PeriodType.Weekend))).IsValid);
        _h.Clock.UtcNow = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        Assert.False((await Online(await Request("2026-12-11", PeriodType.Weekend))).IsValid);
    }

    [Fact]
    public async Task Every_night_must_be_on_or_before_bookings_open_until()
    {
        // Open until Tue 30/11/2027. Midweek Mon 29/11 → Fri 03/12 has nights past it; Midweek 22/11 → 26/11 is fine.
        Assert.True((await Online(await Request("2027-11-22"))).IsValid);
        Assert.Contains("startDate", (await Online(await Request("2027-11-29"))).Errors.Keys);
        // Weekend Fri 26/11 → Mon 29/11: last night 28/11 is inside.
        Assert.True((await Online(await Request("2027-11-26", PeriodType.Weekend))).IsValid);
    }

    [Fact]
    public async Task Blocked_periods_are_rejected_online_with_the_contact_details()
    {
        var r = await Online(await Request("2026-12-14", PeriodType.Week)); // Mon 14/12 → Mon 21/12 overlaps Christmas (20/12)
        Assert.Contains("Bookings over Christmas / New Year 2026–27 must be made by phone or email: 02 4455 6677 / info@berger.test.", r.Errors["startDate"]);
    }

    [Fact]
    public async Task Online_guests_may_not_exceed_berth_but_staff_only_get_a_warning()
    {
        var online = await Online(await Request(adults: 10, children: 3));
        Assert.Contains("numberOfAdults", online.Errors.Keys);
        var staff = await Internal(await Request(adults: 10, children: 3, end: D("2027-03-12")));
        Assert.True(staff.IsValid);
        Assert.Contains(staff.Warnings, w => w.Contains("berth of 12"));
    }

    [Fact]
    public async Task Rooming_warning_must_be_accepted_with_the_exact_text_online()
    {
        var request = await Request(adults: 9, scope: ValidationScope.Booking);
        var draft = await _h.CreateDraftAsync();
        request = request with { Draft = _h.Db.Drafts.Get(draft), TermsAccepted = true };
        Assert.Contains("roomingWarningAccepted", (await Online(request)).Errors.Keys);
        var text = RoomingWarning.Text(_h.Boat("pacific-blue"), 9);
        Assert.Contains("roomingWarningAccepted", (await Online(request with { RoomingWarningAccepted = true, RoomingWarningText = "something else" })).Errors.Keys);
        Assert.True((await Online(request with { RoomingWarningAccepted = true, RoomingWarningText = text })).IsValid);
    }

    [Fact]
    public async Task Terms_and_a_valid_draft_for_this_boat_are_required_to_book()
    {
        var r = await Online(await Request(scope: ValidationScope.Booking));
        Assert.Contains("termsAccepted", r.Errors.Keys);
        Assert.Contains("draftId", r.Errors.Keys);
        _h.Activate("misty-blue");
        var otherBoatDraft = _h.Db.Drafts.Get(await _h.CreateDraftAsync("misty-blue"));
        var mismatch = await Online((await Request(scope: ValidationScope.Booking)) with { Draft = otherBoatDraft, TermsAccepted = true });
        Assert.Contains("draftId", mismatch.Errors.Keys);
    }

    [Fact]
    public async Task Addons_must_be_allowed_active_and_quantity_one_when_quantity_does_not_apply()
    {
        var notAllowed = _h.Boat("pacific-blue");
        notAllowed.SetAllowedAddons(["ice"]);
        var r = await Online(await Request(addons: [new("outboard-motor", 1), new("ice", 0)]));
        Assert.Contains(r.Errors["addons"], e => e.Contains("isn't offered"));
        notAllowed.SetAllowedAddons(_h.Db.Addons.All.Select(a => a.Id));
        var twice = await Online(await Request(addons: [new("outboard-motor", 2)]));
        Assert.Contains("addons", twice.Errors.Keys);
    }

    [Fact]
    public async Task Capacity_is_a_hard_limit_online_and_a_warning_for_staff()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12", lines: [new(null, "car-parking", 9, null)]));
        var online = await Online(await Request(addons: [new("car-parking", 3)]));
        Assert.Equal(["Only 2 car spots remain for your dates."], online.Errors["addons"]);
        Assert.True((await Online(await Request(addons: [new("car-parking", 2)]))).IsValid);

        _h.Activate("misty-blue");
        var staff = await Internal(await Request(addons: [new("car-parking", 3)], end: D("2027-03-12"), slug: "misty-blue"));
        Assert.True(staff.IsValid);
        Assert.Contains("This exceeds car parking capacity on 08/03/2027.", staff.Warnings);
    }

    [Fact]
    public async Task Staff_rules_only_need_dates_guests_and_a_valid_customer()
    {
        var r = await Internal((await Request(adults: 0, end: D("2027-03-08"))) with { Contact = new ContactInput("", "Smith", "nope", "02 9876 5432") });
        Assert.Contains("endDate", r.Errors.Keys);
        Assert.Contains("numberOfAdults", r.Errors.Keys);
        Assert.Contains("firstName", r.Errors.Keys);
        Assert.Contains("email", r.Errors.Keys);
        Assert.Equal([MobileNumber.InvalidMessage], r.Errors["mobile"]);
        // Inside a blocked period and with a short lead time is fine for staff.
        Assert.True((await Internal(await Request("2026-11-10", PeriodType.Custom, end: D("2026-12-24")))).IsValid);
    }

    [Fact]
    public async Task Custom_times_clashing_with_a_same_day_changeover_warn()
    {
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-05", "2027-03-08"));
        var standard = await Internal(await Request(end: D("2027-03-12")));
        Assert.Empty(standard.Warnings);
        var early = await Internal((await Request(end: D("2027-03-12"))) with { CheckInTime = new TimeOnly(7, 0) });
        Assert.Contains(early.Warnings, w => w.Contains("Check-in at 7am on 08/03/2027 is before"));
    }
}

public class CatalogueTests
{
    private readonly Harness _h = new();
    private PublicCatalogueService Catalogue => _h.Get<PublicCatalogueService>();

    [Fact]
    public async Task Fleet_lists_only_active_house_boats_in_berth_order()
    {
        _h.Activate("runaway-bear");
        _h.Activate("misty-blue");
        var boats = await Catalogue.GetBoatsAsync();
        Assert.Equal(["pacific-blue", "misty-blue", "runaway-bear"], boats.Boats.Select(b => b.Slug));
        Assert.Equal(D("2026-11-09"), boats.Settings.Today);
    }

    [Fact]
    public async Task Fleet_availability_returns_ranges_for_blocking_bookings_and_unavailabilities_only()
    {
        var misty = _h.Activate("misty-blue");
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-08", "2027-03-12"));
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-15", "2027-03-19", standby: true, email: "s@example.com"));
        var cancelled = await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-22", "2027-03-26", email: "c@example.com"));
        await _h.Admin.UpdateAsync(cancelled.Booking.Id, _h.UpdateRequest(cancelled.Booking, r => r with { Status = BookingStatus.Cancelled }));
        var draft = await _h.CreateDraftAsync("misty-blue", "2027-04-05");
        await _h.Online.CreateBookingAsync(_h.BookingRequest(draft, "misty-blue", "2027-04-05")); // a live hold
        var lapsedDraft = await _h.CreateDraftAsync("misty-blue", "2027-04-12");
        await _h.Online.CreateBookingAsync(_h.BookingRequest(lapsedDraft, "misty-blue", "2027-04-12"));
        _h.Db.Unavailabilities.Save(BoatUnavailability.Create(misty.Id, D("2027-05-03"), D("2027-05-04"), "Service", _h.Clock.UtcNow));
        _h.Clock.Advance(TimeSpan.FromMinutes(39)); // first hold still live (40 min), …
        var r = await Catalogue.GetAvailabilityAsync(D("2027-03-01"), D("2027-06-01"));
        Assert.Equal([new StayRangeDto(D("2027-03-08"), D("2027-03-12"))], r.Boats.Single(b => b.Slug == "pacific-blue").Unavailable);
        Assert.Equal(3, r.Boats.Single(b => b.Slug == "misty-blue").Unavailable.Count);
        Assert.Contains(new StayRangeDto(D("2027-05-03"), D("2027-05-05")), r.Boats.Single(b => b.Slug == "misty-blue").Unavailable);
        _h.Clock.Advance(TimeSpan.FromMinutes(2)); // … now both holds have lapsed
        var later = await Catalogue.GetAvailabilityAsync(D("2027-03-01"), D("2027-06-01"));
        Assert.Single(later.Boats.Single(b => b.Slug == "misty-blue").Unavailable);
    }

    [Fact]
    public async Task Fleet_availability_lists_blocked_periods_and_limits_the_span()
    {
        var r = await Catalogue.GetAvailabilityAsync(D("2026-12-01"), D("2027-01-31"));
        Assert.Equal("Christmas / New Year 2026–27", r.EnquireOnly.Single().Name);
        await Assert.ThrowsAsync<DomainValidationException>(() => Catalogue.GetAvailabilityAsync(D("2026-12-01"), D("2028-04-01")));
    }

    [Fact]
    public async Task Period_availability_gives_each_boat_availability_and_price()
    {
        _h.Activate("misty-blue");
        await _h.Admin.CreateAsync(_h.StaffRequest("2027-03-09", "2027-03-10"));
        var r = await Catalogue.GetPeriodAvailabilityAsync(PeriodType.Midweek, D("2027-03-08"));
        Assert.Equal(D("2027-03-12"), r.EndDate);
        Assert.Equal(new PeriodBoatAvailability("pacific-blue", false, 4060), r.Boats[0]);
        Assert.Equal(new PeriodBoatAvailability("misty-blue", true, 1500), r.Boats[1]);
        var peak = await Catalogue.GetPeriodAvailabilityAsync(PeriodType.Week, D("2027-01-08"));
        Assert.Equal(6200m, peak.Boats[0].HirePrice);
    }

    [Theory]
    [InlineData("2027-03-09", PeriodType.Midweek)] // shape
    [InlineData("2026-11-16", PeriodType.Midweek)] // lead time
    [InlineData("2027-12-06", PeriodType.Midweek)] // open-until
    [InlineData("2026-12-14", PeriodType.Week)] // blocked period
    [InlineData("2027-03-08", PeriodType.Custom)] // staff-only period type
    public async Task Invalid_online_stays_are_rejected(string start, PeriodType type) =>
        await Assert.ThrowsAsync<DomainValidationException>(() => Catalogue.GetPeriodAvailabilityAsync(type, D(start)));

    [Fact]
    public async Task Addon_remaining_is_404_without_a_capacity()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Catalogue.GetAddonRemainingAsync("ice", D("2027-03-08"), D("2027-03-12")));
        Assert.Equal(11, (await Catalogue.GetAddonRemainingAsync("car-parking", D("2027-03-08"), D("2027-03-12"))).Remaining);
    }

    [Fact]
    public async Task Boat_details_include_allowed_addons_in_order_and_the_effective_deposit()
    {
        var d = await Catalogue.GetBoatAsync("pacific-blue");
        Assert.Equal("yabby-pumps", d.Addons[0].Id);
        Assert.Equal(1000m, d.DepositAmount);
        Assert.True(d.Addons.Single(a => a.Id == "car-parking").HasCapacity);
        Assert.False((await Catalogue.GetBoatAsync("runaway-bear")).IsActive);
        Assert.Equal(250m, (await Catalogue.GetBoatAsync("runaway-bear")).DepositAmount);
        await Assert.ThrowsAsync<NotFoundException>(() => Catalogue.GetBoatAsync("nope"));
    }
}
