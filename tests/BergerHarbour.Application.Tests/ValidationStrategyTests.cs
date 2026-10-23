using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests;

public class ValidationStrategyTests
{
    private static readonly DateTimeOffset Now = TestApp.DefaultNow;
    private static readonly DateOnly Today = new(2026, 10, 5);
    private readonly OnlineBookingValidationStrategy _online = new();
    private readonly InternalBookingValidationStrategy _internal = new();
    private readonly Boat _boat;
    private readonly AddonDefinition _motor = AddonDefinition.Create("Outboard motor", null, false, new AddonPrices(95, 95, 130), false, null, null, 0);
    private readonly AddonDefinition _ice = AddonDefinition.Create("Ice", null, true, null, true, null, null, 1);
    private readonly AddonDefinition _parking = AddonDefinition.Create("Car parking", null, true, null, true, 11, "car spots", 2);
    private readonly AddonDefinition _notAllowed = AddonDefinition.Create("Skipper", null, true, null, false, null, null, 3);
    private readonly AddonDefinition _inactive = AddonDefinition.Create("Heater", null, true, null, false, null, null, 4);
    private readonly BookingDraft _draft;
    private readonly BusinessSettings _settings = TestSettings(minimumLeadTimeDays: 30);
    private readonly List<BlockedPeriod> _blocked =
    [
        BlockedPeriod.Create("Christmas / New Year 2026–27", new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5), true),
    ];

    /// <summary>Settings pinned to explicit values so these tests don't move when the production defaults do.</summary>
    private static BusinessSettings TestSettings(int minimumLeadTimeDays)
    {
        var settings = BusinessSettings.CreateDefault(new DateOnly(2027, 11, 30), "staff@example.test", "02 4444 4444", "info@example.test");
        settings.Update(500m, minimumLeadTimeDays, new DateOnly(2027, 11, 30), "staff@example.test", "02 4444 4444", "info@example.test",
            BusinessSettings.DefaultHireTermsUrl);
        return settings;
    }

    public ValidationStrategyTests()
    {
        var seasons = new List<Season> { Season.CreateNormal() };
        _boat = Boat.Create("Pacific Blue", "pacific-blue", BoatType.House, 12, Now);
        _boat.UpdateDetails("Pacific Blue", "pacific-blue", 12, 6, 2, "4 queens", 2000, null, Now);
        _boat.SetRates(PeriodTypes.Rated.Select(p => new BoatRate(SeasonId.Normal, p, 4000)), Now);
        _boat.SetAllowedAddons([_motor.Id, _ice.Id, _parking.Id, _inactive.Id], Now);
        _boat.Activate(seasons, Now);
        _inactive.Deactivate();
        _draft = BookingDraft.Create(_boat.Id, PeriodType.Midweek, new Stay(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 13)),
            4, 2, "Jane Citizen", "jane@example.com", "0412 345 678", Now);
    }

    private BookingValidationContext Context(BookingValidationScope scope = BookingValidationScope.Submit, Boat? boat = null,
        int parkingRemaining = 11) =>
        new(boat ?? _boat, _settings, _blocked, [_motor, _ice, _parking, _notAllowed, _inactive], Today, scope, _draft,
            new Dictionary<AddonId, int> { [_parking.Id] = parkingRemaining });

    private BookingRequest Valid(PeriodType type = PeriodType.Midweek, DateOnly? start = null, int adults = 4, int children = 2,
        IReadOnlyList<BookingAddonRequest>? addons = null) =>
        new(type, start ?? new DateOnly(2026, 11, 9), null, adults, children, "Jane Citizen", "jane@example.com",
            "0412 345 678", addons ?? [], false, null, true, _draft.Id.Value);

    private IReadOnlyDictionary<string, string[]> Online(BookingRequest r, BookingValidationScope scope = BookingValidationScope.Submit,
        int parkingRemaining = 11) =>
        _online.Validate(r, Context(scope, parkingRemaining: parkingRemaining)).ToDictionary();

    [Fact]
    public void A_valid_online_request_passes() => Assert.Empty(Online(Valid()));

    [Theory]
    [InlineData(PeriodType.Midweek, 2026, 11, 10)] // Tuesday
    [InlineData(PeriodType.Weekend, 2026, 11, 9)] // Monday
    [InlineData(PeriodType.Week, 2026, 11, 11)] // Wednesday
    public void Period_shape_is_enforced(PeriodType type, int y, int m, int d) =>
        Assert.True(Online(Valid(type, new DateOnly(y, m, d))).ContainsKey("startDate"));

    [Theory]
    [InlineData(PeriodType.LongWeekend)]
    [InlineData(PeriodType.Custom)]
    public void Staff_only_period_types_are_rejected_online(PeriodType type) =>
        Assert.True(Online(Valid(type)).ContainsKey("periodType"));

    [Fact]
    public void Lead_time_boundary()
    {
        // Today + 30 = Wed 04/11/2026. The first Monday on or after is 09/11; a Weekend starting Fri 06/11 is fine,
        // and a Week starting Mon 02/11 is too soon.
        Assert.Empty(Online(Valid(PeriodType.Weekend, new DateOnly(2026, 11, 6))));
        Assert.True(Online(Valid(PeriodType.Week, new DateOnly(2026, 11, 2))).ContainsKey("startDate"));
    }

    [Fact]
    public void Lead_time_exact_day_is_allowed()
    {
        var context = Context() with { Settings = TestSettings(minimumLeadTimeDays: 28) };
        // Today + 28 = Monday 02/11/2026.
        Assert.True(_online.Validate(Valid(PeriodType.Week, new DateOnly(2026, 11, 2)), context).IsValid);
        Assert.False(_online.Validate(Valid(PeriodType.Midweek, new DateOnly(2026, 10, 26)), context).IsValid);
    }

    [Fact]
    public void Open_until_boundary()
    {
        // Open until Tue 30/11/2027: a Week Mon 22/11 → Mon 29/11 is inside (last night 28/11),
        // a Midweek Mon 29/11 → Fri 03/12 is not.
        Assert.Empty(Online(Valid(PeriodType.Week, new DateOnly(2027, 11, 22))));
        Assert.True(Online(Valid(PeriodType.Midweek, new DateOnly(2027, 11, 29))).ContainsKey("startDate"));
        // Week Fri 26/11 → Fri 03/12 has last night 02/12 → outside.
        Assert.True(Online(Valid(PeriodType.Week, new DateOnly(2027, 11, 26))).ContainsKey("startDate"));
    }

    [Fact]
    public void Blocked_periods_show_the_enquiry_message()
    {
        // Mid-week Mon 14/12 → Fri 18/12/2026 is fine; Mon 21/12 overlaps Christmas.
        Assert.Empty(Online(Valid(PeriodType.Midweek, new DateOnly(2026, 12, 14))));
        var errors = Online(Valid(PeriodType.Midweek, new DateOnly(2026, 12, 21)));
        Assert.Contains("Bookings over Christmas / New Year 2026–27 must be made by phone or email: 02 4444 4444 / info@example.test.",
            errors["startDate"]);
        // Weekend Fri 18/12 → Mon 21/12 has nights 18, 19, 20: overlaps FirstNight 20/12.
        Assert.True(Online(Valid(PeriodType.Weekend, new DateOnly(2026, 12, 18))).ContainsKey("startDate"));
    }

    [Theory]
    [InlineData(0, 0, "numberOfAdults")]
    [InlineData(0, 4, "numberOfAdults")]
    [InlineData(2, -1, "numberOfChildren")]
    [InlineData(10, 3, "numberOfChildren")]
    public void At_least_one_adult_and_no_more_than_max_guests(int adults, int children, string field) =>
        Assert.True(Online(Valid(adults: adults, children: children)).ContainsKey(field));

    [Fact]
    public void Max_guests_counts_adults_and_children()
    {
        Assert.Empty(Online(Valid(adults: 6, children: 2)));
        var full = Valid(adults: 1, children: 11) with { RoomingWarningAccepted = true, RoomingWarningText = _boat.RoomingWarningText(12) };
        Assert.Empty(Online(full));
    }

    [Fact]
    public void Rooming_warning_must_be_accepted_with_the_exact_text()
    {
        var request = Valid(adults: 7, children: 2);
        Assert.True(Online(request).ContainsKey("roomingWarningAccepted"));
        Assert.True(Online(request with { RoomingWarningAccepted = true, RoomingWarningText = "something else" })
            .ContainsKey("roomingWarningAccepted"));
        Assert.Empty(Online(request with { RoomingWarningAccepted = true, RoomingWarningText = _boat.RoomingWarningText(9) }));
        Assert.Empty(Online(Valid(adults: 6, children: 2)));
    }

    [Fact]
    public void A_valid_unconverted_draft_for_this_boat_is_required()
    {
        Assert.True(Online(Valid() with { DraftId = null }).ContainsKey("draftId"));
        Assert.True(Online(Valid() with { DraftId = "other" }).ContainsKey("draftId"));
        Assert.True(_online.Validate(Valid(), Context() with { Draft = null }).ToDictionary().ContainsKey("draftId"));

        var otherBoat = BookingDraft.Create(BoatId.New(), PeriodType.Midweek, _draft.Stay, 1, 0, "Jane", "jane@example.com", "0412 345 678", Now);
        Assert.True(_online.Validate(Valid() with { DraftId = otherBoat.Id.Value }, Context() with { Draft = otherBoat })
            .ToDictionary().ContainsKey("draftId"));

        _draft.MarkConverted(BookingId.New(), Now);
        Assert.True(Online(Valid()).ContainsKey("draftId"));
    }

    [Fact]
    public void Capacity_limited_add_ons_are_a_hard_limit_online()
    {
        Assert.Empty(Online(Valid(addons: [new(_parking.Id.Value, 3)]), parkingRemaining: 3));
        var errors = Online(Valid(addons: [new(_parking.Id.Value, 4)]), parkingRemaining: 3);
        Assert.Contains("Only 3 car spots remain for your dates.", errors["addons"]);
        Assert.Contains("Car parking is fully booked for your dates.",
            Online(Valid(addons: [new(_parking.Id.Value, 1)]), parkingRemaining: 0)["addons"]);
    }

    [Fact]
    public void Terms_must_be_accepted() =>
        Assert.True(Online(Valid() with { TermsAccepted = false }).ContainsKey("termsAccepted"));

    [Fact]
    public void Contact_details_are_required()
    {
        var errors = Online(Valid() with { FullName = " ", Email = "bad", Mobile = "" });
        Assert.True(errors.ContainsKey("fullName"));
        Assert.True(errors.ContainsKey("email"));
        Assert.True(errors.ContainsKey("mobile"));
    }

    [Theory]
    [InlineData("0412 345 678", true)]
    [InlineData("+44 7911 123456", true)]
    [InlineData("+64 21 123 4567", true)]
    [InlineData("0400", false)]
    [InlineData("+44 12", false)]
    public void Mobile_must_be_valid_for_its_country(string mobile, bool valid) =>
        Assert.Equal(valid, !Online(Valid() with { Mobile = mobile }).ContainsKey("mobile"));

    [Fact]
    public void Addons_must_be_active_allowed_and_sensibly_quantified()
    {
        Assert.Empty(Online(Valid(addons: [new(_motor.Id.Value, 1), new(_ice.Id.Value, 4)])));
        Assert.True(Online(Valid(addons: [new(_notAllowed.Id.Value, 1)])).ContainsKey("addons"));
        Assert.True(Online(Valid(addons: [new(_inactive.Id.Value, 1)])).ContainsKey("addons"));
        Assert.True(Online(Valid(addons: [new(_motor.Id.Value, 2)])).ContainsKey("addons"));
        Assert.True(Online(Valid(addons: [new(_ice.Id.Value, 0)])).ContainsKey("addons"));
        Assert.True(Online(Valid(addons: [new("nope", 1)])).ContainsKey("addons"));
    }

    [Fact]
    public void Inactive_boat_cannot_be_booked_online()
    {
        var boat = Boat.Create("Kalinda", "kalinda", BoatType.House, 12, Now);
        var errors = _online.Validate(Valid(), Context(boat: boat)).ToDictionary();
        Assert.Contains("currently unavailable", errors["slug"][0]);
    }

    [Fact]
    public void Quote_scope_skips_the_draft_acceptance_terms_and_contact_details()
    {
        var request = new BookingRequest(PeriodType.Midweek, new DateOnly(2026, 11, 9), null, 8, 2, null, null, null, []);
        Assert.Empty(Online(request, BookingValidationScope.Quote));
        Assert.NotEmpty(Online(request));
    }

    [Fact]
    public void Internal_strategy_only_checks_dates_guests_and_customer()
    {
        var context = Context() with { Draft = null };
        // Christmas, inside the lead time, odd shape, 20 guests: all fine for staff.
        var ok = new BookingRequest(PeriodType.Custom, new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 29), 14, 6,
            "Jane", "jane@example.com", null, []);
        Assert.True(_internal.Validate(ok, context).IsValid);
        Assert.True(_internal.Validate(ok with { StartDate = new DateOnly(2026, 10, 6), EndDate = new DateOnly(2026, 10, 7) }, context).IsValid);

        var bad = _internal.Validate(ok with
        {
            EndDate = new DateOnly(2026, 12, 22), NumberOfAdults = 0, NumberOfChildren = -1, Email = null, FullName = "", Mobile = "12",
        }, context).ToDictionary();
        Assert.Equal(["endDate", "numberOfAdults", "numberOfChildren", "fullName", "email", "phone"], bad.Keys.ToArray());
    }
}
