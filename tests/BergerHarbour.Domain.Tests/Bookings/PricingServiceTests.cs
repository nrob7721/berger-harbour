using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class PricingServiceTests
{
    private static readonly Season Normal = Season.CreateDefault(new SeasonId("normal"));
    private static readonly Season OffPeak = Season.Create(new SeasonId("off-peak"), "Off Peak",
        [new SeasonRange(DayMonth.Create(1, 5), DayMonth.Create(31, 8))], [Normal]);
    private static readonly Season Peak = Season.Create(new SeasonId("peak"), "Peak",
        [new SeasonRange(DayMonth.Create(1, 12), DayMonth.Create(31, 1))], [Normal, OffPeak]);
    private static readonly IReadOnlyList<Season> Seasons = [Normal, OffPeak, Peak];

    private readonly PricingService pricing = new();

    [Theory]
    [InlineData("2027-03-08", "Normal")]
    [InlineData("2027-05-01", "Off Peak")]
    [InlineData("2027-08-31", "Off Peak")]
    [InlineData("2027-09-01", "Normal")]
    [InlineData("2026-12-01", "Peak")]
    [InlineData("2027-01-31", "Peak")]
    [InlineData("2027-02-01", "Normal")]
    public void Season_is_the_one_containing_the_start_date_including_the_year_wrapping_peak(string start, string season) =>
        Assert.Equal(season, pricing.SeasonFor(D(start), Seasons).Name);

    [Fact]
    public void Hire_price_is_the_boats_rate_for_season_and_period_type()
    {
        var boat = PacificBlue();
        Assert.Equal(4060m, pricing.HirePrice(boat, PeriodType.Midweek, D("2027-03-08"), Seasons));
        Assert.Equal(5250m, pricing.HirePrice(boat, PeriodType.Week, D("2027-06-07"), Seasons));
        Assert.Equal(5100m, pricing.HirePrice(boat, PeriodType.LongWeekend, D("2027-01-22"), Seasons));
        Assert.Null(pricing.HirePrice(boat, PeriodType.Custom, D("2027-03-08"), Seasons));
    }

    [Fact]
    public void Addon_prices_by_period_type_none_for_price_on_request_long_weekend_and_custom()
    {
        var motor = AddonDefinition.Create(new AddonId("motor"), "Outboard motor", null, false, new AddonPrices(95, 95, 130), false, null, null, 0);
        var hamper = AddonDefinition.Create(new AddonId("hamper"), "Welcome hamper", null, true, new AddonPrices(10, 10, 10), false, null, null, 1);
        Assert.Equal(95m, pricing.AddonUnitPrice(motor, PeriodType.Midweek));
        Assert.Equal(130m, pricing.AddonUnitPrice(motor, PeriodType.Week));
        Assert.Null(pricing.AddonUnitPrice(motor, PeriodType.LongWeekend));
        Assert.Null(pricing.AddonUnitPrice(motor, PeriodType.Custom));
        Assert.Null(pricing.AddonUnitPrice(hamper, PeriodType.Midweek));
    }

    [Fact]
    public void The_price_is_snapshotted_onto_the_booking()
    {
        var booking = Support.TestBookings.Staff("2027-03-08", "2027-03-12", hire: 4060);
        Assert.Equal(4060m, booking.HirePrice);
        Assert.Equal(4060m, booking.TotalPrice);
    }

    internal static Boat PacificBlue()
    {
        IEnumerable<BoatRate> Rates(Season s, decimal w, decimal m, decimal lw, decimal wk) =>
        [
            new(s.Id, PeriodType.Weekend, w), new(s.Id, PeriodType.Midweek, m), new(s.Id, PeriodType.LongWeekend, lw), new(s.Id, PeriodType.Week, wk),
        ];
        return Boat.Create(new BoatId("pacific-blue"), new BoatDetails("Pacific Blue", "pacific-blue", BoatType.House, 12, 6, 2, "4 queen bedrooms", 2000,
            null, null, true, false, [.. Rates(Normal, 4060, 4060, 4620, 5850), .. Rates(OffPeak, 3570, 3570, 4050, 5250), .. Rates(Peak, 4500, 4500, 5100, 6200)],
            []), DateTimeOffset.UnixEpoch);
    }
}
