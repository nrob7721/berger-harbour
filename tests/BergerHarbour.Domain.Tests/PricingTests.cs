using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class PricingTests
{
    private readonly List<Season> _seasons = Seasons();

    [Theory]
    [InlineData(2026, 11, 30, "Normal")]
    [InlineData(2026, 12, 1, "Peak")]
    [InlineData(2026, 12, 31, "Peak")]
    [InlineData(2027, 1, 1, "Peak")] // wraps the year end
    [InlineData(2027, 1, 31, "Peak")]
    [InlineData(2027, 2, 1, "Normal")]
    [InlineData(2027, 4, 30, "Normal")]
    [InlineData(2027, 5, 1, "Off Peak")]
    [InlineData(2027, 8, 31, "Off Peak")]
    [InlineData(2027, 9, 1, "Normal")]
    public void Season_lookup(int y, int m, int d, string expected) =>
        Assert.Equal(expected, PricingService.SeasonFor(new DateOnly(y, m, d), _seasons).Name);

    [Fact]
    public void Hire_price_uses_the_season_of_the_start_date()
    {
        var boat = CompleteBoat(_seasons);
        // Fri 27/11/2026 weekend starts in Normal even though it ends in... still November; Mon 30/11 midweek → Normal.
        Assert.Equal(4060m, PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Weekend, D(2026, 11, 27)).Price);
        // Mon 30/11/2026 week ends in Peak but starts in Normal.
        Assert.Equal(5850m, PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Week, D(2026, 11, 30)).Price);
        Assert.Equal(6200m, PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Week, D(2026, 12, 4)).Price);
        Assert.Equal(3570m, PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Midweek, D(2027, 5, 3)).Price);
        Assert.Equal(5100m, PricingService.QuoteHirePrice(boat, _seasons, PeriodType.LongWeekend, D(2027, 1, 22)).Price);
    }

    [Fact]
    public void Custom_stays_are_priced_by_staff()
    {
        var boat = CompleteBoat(_seasons);
        Assert.Throws<DomainValidationException>(() => PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Custom, D(2026, 12, 20)));
    }

    [Fact]
    public void Missing_rate_is_reported()
    {
        var boat = CompleteBoat([Normal()]);
        var ex = Assert.Throws<DomainValidationException>(() =>
            PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Week, D(2026, 12, 4)));
        Assert.Contains("Peak", ex.Message);
    }

    [Fact]
    public void Price_is_snapshotted_on_the_booking()
    {
        var boat = CompleteBoat(_seasons);
        var quote = PricingService.QuoteHirePrice(boat, _seasons, PeriodType.Midweek, D(2026, 11, 9));
        var booking = StaffBooking(boat.Id, D(2026, 11, 9), D(2026, 11, 13), hirePrice: quote.Price);

        boat.SetRates([.. boat.Rates.Select(r => new Domain.Boats.BoatRate(r.SeasonId, r.PeriodType, r.Price + 100))], Now);

        Assert.Equal(4060m, booking.HirePrice);
    }

    [Fact]
    public void Addon_pricing_by_period_type()
    {
        var motor = AddonDefinition.Create("Outboard motor", null, false, new AddonPrices(95m, 95m, 130m), false);
        Assert.Equal(95m, PricingService.AddonUnitPrice(motor, PeriodType.Midweek));
        Assert.Equal(95m, PricingService.AddonUnitPrice(motor, PeriodType.Weekend));
        Assert.Equal(130m, PricingService.AddonUnitPrice(motor, PeriodType.Week));
        Assert.Equal(95m, PricingService.AddonUnitPrice(motor, PeriodType.LongWeekend));
        Assert.Null(PricingService.AddonUnitPrice(motor, PeriodType.Custom));
    }

    [Fact]
    public void Price_on_request_addons_have_no_price()
    {
        var hamper = AddonDefinition.Create("Welcome hamper", null, true, new AddonPrices(1m, 1m, 1m), false);
        Assert.Null(hamper.Prices);
        Assert.Null(PricingService.AddonUnitPrice(hamper, PeriodType.Week));
    }

    [Fact]
    public void Priced_addon_requires_prices() =>
        Assert.Throws<DomainValidationException>(() => AddonDefinition.Create("Ice", null, false, null, true));

    [Fact]
    public void Quantity_is_always_one_when_quantity_does_not_apply()
    {
        var motor = AddonDefinition.Create("Outboard motor", null, false, new AddonPrices(95m, 95m, 130m), false);
        var ice = AddonDefinition.Create("Ice", null, true, null, true);
        Assert.Equal(1, motor.NormaliseQuantity(5));
        Assert.Equal(5, ice.NormaliseQuantity(5));
        Assert.Throws<DomainValidationException>(() => ice.NormaliseQuantity(0));
    }

    [Fact]
    public void Requested_addons_are_not_in_the_total_until_confirmed()
    {
        var booking = StaffBooking(BoatId.New(), D(2026, 11, 9), D(2026, 11, 13), hirePrice: 4060m);
        var line = booking.AddAddon(new RequestedAddon(AddonId.New(), "Outboard motor", 1, 95m), Now);
        Assert.Equal(4060m, booking.TotalPrice);

        booking.UpdateAddonLine(line.Id, 1, 95m, AddonLineStatus.Confirmed, Now);
        Assert.Equal(4155m, booking.TotalPrice);

        booking.UpdateAddonLine(line.Id, 1, 95m, AddonLineStatus.Declined, Now);
        Assert.Equal(4060m, booking.TotalPrice);
    }

    [Fact]
    public void Price_on_request_line_needs_a_price_before_confirming()
    {
        var booking = StaffBooking(BoatId.New(), D(2026, 11, 9), D(2026, 11, 13));
        var line = booking.AddAddon(new RequestedAddon(AddonId.New(), "Welcome hamper", 1, null), Now);
        Assert.Throws<DomainValidationException>(() => booking.UpdateAddonLine(line.Id, 1, null, AddonLineStatus.Confirmed, Now));
        booking.UpdateAddonLine(line.Id, 2, 150m, AddonLineStatus.Confirmed, Now);
        Assert.Equal(booking.HirePrice + 300m, booking.TotalPrice);
    }

    [Fact]
    public void Season_ranges_cannot_overlap_within_a_season() =>
        Assert.Throws<DomainValidationException>(() => Season.Create("Bad",
        [
            new DayMonthRange(new DayMonth(1, 5), new DayMonth(31, 8)),
            new DayMonthRange(new DayMonth(1, 8), new DayMonth(15, 8)),
        ]));

    [Fact]
    public void Season_ranges_cannot_overlap_across_seasons()
    {
        var school = Season.Create("School holidays", [new DayMonthRange(new DayMonth(25, 1), new DayMonth(5, 2))]);
        Assert.Throws<DomainValidationException>(() => SeasonCatalogue.EnsureNoOverlap(school, _seasons));
        var ok = Season.Create("Spring", [new DayMonthRange(new DayMonth(1, 9), new DayMonth(30, 9))]);
        SeasonCatalogue.EnsureNoOverlap(ok, _seasons);
    }

    [Fact]
    public void Normal_season_cannot_be_deleted_or_given_ranges()
    {
        var normal = Normal();
        Assert.Throws<DomainValidationException>(normal.EnsureCanBeDeleted);
        Assert.Throws<DomainValidationException>(() => normal.Update("Normal", [new DayMonthRange(new DayMonth(1, 1), new DayMonth(2, 1))]));
    }

    [Fact]
    public void Day_month_parsing()
    {
        Assert.Equal(new DayMonth(1, 12), DayMonth.Parse("01-12"));
        Assert.Equal(new DayMonth(29, 2), DayMonth.Parse("29-02"));
        Assert.Throws<DomainValidationException>(() => DayMonth.Parse("31-02"));
        Assert.Throws<DomainValidationException>(() => DayMonth.Parse("12/01"));
    }
}
