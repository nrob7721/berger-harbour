using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class StayPeriodFactoryTests
{
    private readonly StayPeriodFactory factory = new();

    // Mon 12/10/2026 … Sun 18/10/2026; Mon 19/10/2026.
    [Theory]
    [InlineData("2026-10-12", "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-13", "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-14", "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-15", "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-16", "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-17", null, null)]
    [InlineData("2026-10-18", null, null)]
    public void Midweek_click_selects_mon_to_fri_of_that_week(string clicked, string? start, string? end) =>
        AssertStay(factory.ForClickedDay(PeriodType.Midweek, D(clicked)), start, end);

    [Theory]
    [InlineData("2026-10-16", "2026-10-16", "2026-10-19")]
    [InlineData("2026-10-17", "2026-10-16", "2026-10-19")]
    [InlineData("2026-10-18", "2026-10-16", "2026-10-19")]
    [InlineData("2026-10-19", "2026-10-16", "2026-10-19")]
    [InlineData("2026-10-12", "2026-10-09", "2026-10-12")]
    [InlineData("2026-10-13", null, null)]
    [InlineData("2026-10-14", null, null)]
    [InlineData("2026-10-15", null, null)]
    public void Weekend_click_selects_the_fri_to_mon_containing_it(string clicked, string? start, string? end) =>
        AssertStay(factory.ForClickedDay(PeriodType.Weekend, D(clicked)), start, end);

    [Theory]
    [InlineData("2026-10-12", "2026-10-12", "2026-10-19")]
    [InlineData("2026-10-16", "2026-10-16", "2026-10-23")]
    [InlineData("2026-10-13", null, null)]
    [InlineData("2026-10-14", null, null)]
    [InlineData("2026-10-15", null, null)]
    [InlineData("2026-10-17", null, null)]
    [InlineData("2026-10-18", null, null)]
    public void Week_click_starts_on_the_clicked_monday_or_friday(string clicked, string? start, string? end) =>
        AssertStay(factory.ForClickedDay(PeriodType.Week, D(clicked)), start, end);

    [Theory]
    [InlineData("2026-10-12", PeriodType.Midweek, "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-15", PeriodType.Midweek, "2026-10-12", "2026-10-16")]
    [InlineData("2026-10-16", PeriodType.Weekend, "2026-10-16", "2026-10-19")]
    [InlineData("2026-10-18", PeriodType.Weekend, "2026-10-16", "2026-10-19")]
    public void Admin_default_for_a_day_is_midweek_mon_to_thu_and_weekend_fri_to_sun(string day, PeriodType type, string start, string end)
    {
        var (periodType, stay) = factory.DefaultForDay(D(day));
        Assert.Equal(type, periodType);
        AssertStay(stay, start, end);
    }

    [Theory]
    [InlineData(PeriodType.Midweek, "2026-10-12", "2026-10-16")]
    [InlineData(PeriodType.Weekend, "2026-10-16", "2026-10-19")]
    [InlineData(PeriodType.Week, "2026-10-12", "2026-10-19")]
    [InlineData(PeriodType.Week, "2026-10-16", "2026-10-23")]
    public void Online_start_gives_the_period_end(PeriodType type, string start, string end) =>
        AssertStay(factory.ForOnlineStart(type, D(start)), start, end);

    [Theory]
    [InlineData(PeriodType.Midweek, "2026-10-13")]
    [InlineData(PeriodType.Weekend, "2026-10-12")]
    [InlineData(PeriodType.Week, "2026-10-14")]
    public void Online_start_on_the_wrong_day_is_a_field_error(PeriodType type, string start)
    {
        var e = Assert.Throws<DomainValidationException>(() => factory.ForOnlineStart(type, D(start)));
        Assert.True(e.Errors.Has("startDate"));
    }

    [Theory]
    [InlineData(PeriodType.LongWeekend)]
    [InlineData(PeriodType.Custom)]
    public void Staff_only_period_types_are_not_online(PeriodType type) =>
        Assert.Throws<DomainValidationException>(() => factory.ForOnlineStart(type, D("2026-10-12")));

    private static void AssertStay(StayRange? stay, string? start, string? end)
    {
        if (start is null)
        {
            Assert.Null(stay);
            return;
        }

        Assert.NotNull(stay);
        Assert.Equal(D(start), stay!.Value.Start);
        Assert.Equal(D(end!), stay.Value.End);
    }
}
