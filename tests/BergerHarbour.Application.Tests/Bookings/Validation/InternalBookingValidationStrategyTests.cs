using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings.Validation;

public class InternalBookingValidationStrategyTests
{
    private static readonly BookingValidationContext Context = new(null,
        BusinessSettings.Create(1000, 30, D("2027-11-30"), null, null, null, null, null, null, null), [], [], D("2026-11-09"),
        BookingValidationScope.Submit, null, new Dictionary<AddonId, int>());

    private readonly InternalBookingValidationStrategy strategy = new();

    private static BookingRequest Request() => new(PeriodType.Custom, D("2026-12-24"), D("2026-12-31"), 2, 0, "John", "Smith", "john@example.com",
        null, null, null, [], false, null, false, null);

    [Fact]
    public void Staff_may_book_inside_the_lead_time_over_blocked_periods_and_above_berth() =>
        Assert.True(strategy.Validate(Request() with { NumberOfAdults = 40 }, Context).IsEmpty);

    [Fact]
    public void Start_before_end_and_guest_minimums()
    {
        var errors = strategy.Validate(Request() with { EndDate = D("2026-12-24"), NumberOfAdults = 0, NumberOfChildren = -1 }, Context);
        Assert.True(errors.Has("endDate"));
        Assert.True(errors.Has("numberOfAdults"));
        Assert.True(errors.Has("numberOfChildren"));
    }

    [Fact]
    public void Customer_names_and_a_valid_email_are_required_mobiles_optional_but_valid_and_the_address_follows_its_rules()
    {
        var errors = strategy.Validate(Request() with
        {
            FirstName = null, LastName = "", Email = "jane@gmail", Mobile = "+61295551234", SecondaryMobile = "nope",
            Address = new AddressDto(new string('a', 101), null, null, "Sydney", null, "AU"),
        }, Context);
        Assert.Equal(["address.line1", "address.state", "email", "firstName", "lastName", "mobile", "secondaryMobile"], errors.ByField.Keys.Order());
        Assert.True(strategy.Validate(Request() with { Mobile = null, SecondaryMobile = "" }, Context).IsEmpty);
    }
}
