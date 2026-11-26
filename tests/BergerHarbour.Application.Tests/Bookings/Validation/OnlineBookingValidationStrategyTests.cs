using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings.Validation;

public class OnlineBookingValidationStrategyTests
{
    private static readonly AddonId Parking = new("car-parking");
    private static readonly AddonId Ice = new("ice");
    private static readonly Season Normal = Season.CreateDefault(new SeasonId("normal"));
    private static readonly BusinessSettings Settings =
        BusinessSettings.Create(1000, 30, D("2027-11-30"), null, "02 4455 6677", "info@example.com", null, null, null, null);
    private static readonly IReadOnlyList<AddonDefinition> Addons =
    [
        AddonDefinition.Create(Parking, "Car parking", null, true, AddonPrices.None, true, 11, "car spots", 0),
        AddonDefinition.Create(Ice, "Ice", null, true, AddonPrices.None, false, null, null, 1),
    ];

    private readonly OnlineBookingValidationStrategy strategy = new();
    private readonly Boat boat = PacificBlue();

    [Fact]
    public void A_valid_submit_passes() => Assert.True(Validate(Request()).IsEmpty);

    [Fact]
    public void Inactive_or_bbq_boats_are_rejected()
    {
        var inactive = Boat.Create(BoatId.New(), Details(), DateTimeOffset.UnixEpoch);
        Assert.True(strategy.Validate(Request(), Context(inactive)).Has("slug"));
        Assert.True(strategy.Validate(Request(), Context(null)).Has("slug"));
    }

    [Theory]
    [InlineData(PeriodType.Midweek, "2027-03-09")]
    [InlineData(PeriodType.LongWeekend, "2027-03-05")]
    public void Period_shape(PeriodType type, string start) => Assert.True(Validate(Request() with { PeriodType = type, StartDate = D(start) }).Has(
        type.IsOnline() ? "startDate" : "periodType"));

    [Fact]
    public void Lead_time_and_open_until() =>
        Assert.True(Validate(Request() with { StartDate = D("2026-11-30") }).Has("startDate"));

    [Fact]
    public void Blocked_periods()
    {
        var christmas = BlockedPeriod.Create(BlockedPeriodId.New(), "Christmas", NightRange.Create(D("2026-12-20"), D("2027-01-05")), true);
        var errors = strategy.Validate(Request() with { PeriodType = PeriodType.Weekend, StartDate = D("2026-12-18") },
            Context(boat) with { BlockedPeriods = [christmas] });
        Assert.Contains(errors.Messages, m => m.StartsWith("Bookings over Christmas", StringComparison.Ordinal));
    }

    [Fact]
    public void Adults_at_least_one_and_guests_within_berth()
    {
        Assert.True(Validate(Request() with { NumberOfAdults = 0 }).Has("numberOfAdults"));
        Assert.True(Validate(Request() with { NumberOfAdults = 10, NumberOfChildren = 3, RoomingWarningAccepted = true,
            RoomingWarningText = boat.RoomingWarningFor(13) }).Has("numberOfChildren"));
        Assert.False(Validate(Request() with { NumberOfAdults = 10, NumberOfChildren = 2, RoomingWarningAccepted = true,
            RoomingWarningText = boat.RoomingWarningFor(12) }).Has("numberOfChildren"));
    }

    [Fact]
    public void Rooming_warning_must_be_accepted_with_the_servers_text_when_guests_exceed_beds()
    {
        Assert.True(Validate(Request() with { NumberOfAdults = 9 }).Has("roomingWarningAccepted"));
        Assert.True(Validate(Request() with { NumberOfAdults = 9, RoomingWarningAccepted = true, RoomingWarningText = "something else" })
            .Has("roomingWarningText"));
        Assert.True(Validate(Request() with { NumberOfAdults = 9, RoomingWarningAccepted = true, RoomingWarningText = boat.RoomingWarningFor(9) }).IsEmpty);
    }

    [Fact]
    public void Terms_must_be_accepted() => Assert.True(Validate(Request() with { TermsAccepted = false }).Has("termsAccepted"));

    [Fact]
    public void Addons_must_be_allowed_active_and_correctly_quantified()
    {
        Assert.True(Validate(Request() with { Addons = [new BookingRequestAddon(new AddonId("unknown"), 1)] }).Has("addons"));
        Assert.True(Validate(Request() with { Addons = [new BookingRequestAddon(Ice, 2)] }).Has("addons"));
        Assert.True(Validate(Request() with { Addons = [new BookingRequestAddon(Ice, 1)] }).IsEmpty);
    }

    [Fact]
    public void Capacity_is_a_hard_limit_online()
    {
        var errors = strategy.Validate(Request() with { Addons = [new BookingRequestAddon(Parking, 7)] },
            Context(boat) with { RemainingCapacity = new Dictionary<AddonId, int> { [Parking] = 6 } });
        Assert.Equal(["Only 6 car spots remain for your dates."], errors.ByField["addons"]);
    }

    [Fact]
    public void Contact_details_are_required_and_valid()
    {
        var errors = Validate(Request() with { FirstName = " ", Email = "jane@gmail", Mobile = "+61295551234" });
        Assert.True(errors.Has("firstName"));
        Assert.True(errors.Has("email"));
        Assert.True(errors.Has("mobile"));
    }

    [Fact]
    public void A_valid_unconverted_draft_for_this_boat_is_required()
    {
        Assert.True(strategy.Validate(Request(), Context(boat) with { Draft = null }).Has("draftId"));
        var draft = Draft(boat.Id);
        draft.MarkConverted(new Domain.Bookings.BookingId("b"), DateTimeOffset.UnixEpoch);
        Assert.True(strategy.Validate(Request() with { DraftId = draft.Id }, Context(boat) with { Draft = draft }).Has("draftId"));
        var otherBoat = Draft(new BoatId("misty-blue"));
        Assert.True(strategy.Validate(Request() with { DraftId = otherBoat.Id }, Context(boat) with { Draft = otherBoat }).Has("draftId"));
    }

    [Fact]
    public void Quote_scope_skips_draft_terms_acceptance_and_contact_details()
    {
        var request = Request() with { TermsAccepted = false, FirstName = null, Email = null, Mobile = null, DraftId = null, NumberOfAdults = 9 };
        Assert.True(strategy.Validate(request, Context(boat) with { Scope = BookingValidationScope.Quote, Draft = null }).IsEmpty);
    }

    private static BoatDetails Details() => new("Pacific Blue", "pacific-blue", BoatType.House, 12, 6, 2, "4 queens", 2000, null, null, false, false,
        PeriodTypes.Online.Select(t => new BoatRate(Normal.Id, t, 4060)).ToList(), [Parking, Ice]);

    private static Boat PacificBlue()
    {
        var b = Boat.Create(new BoatId("pacific-blue"), Details(), DateTimeOffset.UnixEpoch);
        b.Activate([Normal], DateTimeOffset.UnixEpoch);
        return b;
    }

    private static BookingDraft Draft(BoatId boatId) => BookingDraft.Create(new BookingDraftId("draft-1"), new DraftContents(boatId, PeriodType.Midweek,
        StayRange.Create(D("2027-03-08"), D("2027-03-12")), 2, 0, "Jane", "Smith", EmailAddress.Create("jane@example.com"),
        MobileNumber.Create("0412345678"), [], DraftStep.Summary), DateTimeOffset.UnixEpoch);

    private static BookingRequest Request() => new(PeriodType.Midweek, D("2027-03-08"), null, 2, 0, "Jane", "Smith", "jane@example.com", "+61412345678",
        null, null, [], false, null, true, new BookingDraftId("draft-1"));

    private BookingValidationContext Context(Boat? b) =>
        new(b, Settings, [], Addons, D("2026-11-09"), BookingValidationScope.Submit, Draft(boat.Id), new Dictionary<AddonId, int>());

    private ValidationErrors Validate(BookingRequest request) => strategy.Validate(request, Context(boat));
}
