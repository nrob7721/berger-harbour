using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class StayPeriodFactoryTests
{
    // Week of Mon 12/10/2026 … Sun 18/10/2026, next Monday 19/10/2026.
    [Theory]
    [InlineData(12, 12, 16)] // Mon → Mon–Fri
    [InlineData(13, 12, 16)] // Tue
    [InlineData(14, 12, 16)] // Wed
    [InlineData(15, 12, 16)] // Thu
    [InlineData(16, 12, 16)] // Fri → the Mon→Fri ending that day
    public void Midweek_maps_weekdays_to_the_containing_mon_to_fri(int clickedDay, int startDay, int endDay)
    {
        var stay = StayPeriodFactory.FromClickedDay(PeriodType.Midweek, D(2026, 10, clickedDay));
        Assert.Equal(new Stay(D(2026, 10, startDay), D(2026, 10, endDay)), stay);
    }

    [Theory]
    [InlineData(17)] // Sat
    [InlineData(18)] // Sun
    public void Midweek_weekend_days_are_not_clickable(int clickedDay) =>
        Assert.Null(StayPeriodFactory.FromClickedDay(PeriodType.Midweek, D(2026, 10, clickedDay)));

    [Theory]
    [InlineData(16, 16, 19)] // Fri → Fri–Mon
    [InlineData(17, 16, 19)] // Sat
    [InlineData(18, 16, 19)] // Sun
    [InlineData(19, 16, 19)] // Mon → the weekend ending that day
    public void Weekend_maps_fri_to_mon_days_to_the_containing_weekend(int clickedDay, int startDay, int endDay)
    {
        var stay = StayPeriodFactory.FromClickedDay(PeriodType.Weekend, D(2026, 10, clickedDay));
        Assert.Equal(new Stay(D(2026, 10, startDay), D(2026, 10, endDay)), stay);
    }

    [Theory]
    [InlineData(13)] // Tue
    [InlineData(14)] // Wed
    [InlineData(15)] // Thu
    public void Weekend_tue_to_thu_are_not_clickable(int clickedDay) =>
        Assert.Null(StayPeriodFactory.FromClickedDay(PeriodType.Weekend, D(2026, 10, clickedDay)));

    [Theory]
    [InlineData(12, 19)] // Mon → next Mon
    [InlineData(16, 23)] // Fri → next Fri
    public void Week_starts_on_the_clicked_monday_or_friday(int clickedDay, int endDay)
    {
        var stay = StayPeriodFactory.FromClickedDay(PeriodType.Week, D(2026, 10, clickedDay));
        Assert.Equal(new Stay(D(2026, 10, clickedDay), D(2026, 10, endDay)), stay);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(18)]
    public void Week_only_mondays_and_fridays_are_clickable(int clickedDay) =>
        Assert.Null(StayPeriodFactory.FromClickedDay(PeriodType.Week, D(2026, 10, clickedDay)));

    [Theory]
    [InlineData(PeriodType.LongWeekend)]
    [InlineData(PeriodType.Custom)]
    public void Staff_only_period_types_are_never_clickable(PeriodType type)
    {
        for (var day = 12; day <= 18; day++)
        {
            Assert.Null(StayPeriodFactory.FromClickedDay(type, D(2026, 10, day)));
        }
    }

    [Fact]
    public void Midweek_across_a_month_boundary()
    {
        // Thu 01/10/2026 belongs to Mon 28/09 → Fri 02/10.
        Assert.Equal(new Stay(D(2026, 9, 28), D(2026, 10, 2)), StayPeriodFactory.FromClickedDay(PeriodType.Midweek, D(2026, 10, 1)));
    }

    [Theory]
    [InlineData(PeriodType.Midweek, 12, 16, true)]
    [InlineData(PeriodType.Midweek, 13, 17, false)]
    [InlineData(PeriodType.Midweek, 12, 19, false)]
    [InlineData(PeriodType.Weekend, 16, 19, true)]
    [InlineData(PeriodType.Weekend, 17, 20, false)]
    [InlineData(PeriodType.Week, 12, 19, true)]
    [InlineData(PeriodType.Week, 16, 23, true)]
    [InlineData(PeriodType.Week, 13, 20, false)]
    [InlineData(PeriodType.Week, 12, 16, false)]
    [InlineData(PeriodType.LongWeekend, 16, 20, false)]
    public void Online_shape(PeriodType type, int startDay, int endDay, bool valid) =>
        Assert.Equal(valid, StayPeriodFactory.IsValidOnlineShape(type, new Stay(D(2026, 10, startDay), D(2026, 10, endDay))));

    [Fact]
    public void Default_end_dates_for_the_staff_dialog()
    {
        var mon = D(2026, 10, 12);
        Assert.Equal(D(2026, 10, 16), StayPeriodFactory.DefaultEndDate(PeriodType.Midweek, mon));
        Assert.Equal(D(2026, 10, 19), StayPeriodFactory.DefaultEndDate(PeriodType.Weekend, D(2026, 10, 16)));
        Assert.Equal(D(2026, 10, 19), StayPeriodFactory.DefaultEndDate(PeriodType.Week, mon));
        Assert.Equal(D(2026, 10, 20), StayPeriodFactory.DefaultEndDate(PeriodType.LongWeekend, D(2026, 10, 16)));
        Assert.Null(StayPeriodFactory.DefaultEndDate(PeriodType.Custom, mon));
    }
}
