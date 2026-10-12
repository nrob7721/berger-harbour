using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class RebookPlannerTests
{
    [Fact]
    public void Moves_to_the_next_year_in_which_the_stay_starts_today_or_later()
    {
        // 23/10/2021 → 28/10/2021, re-booked on 10/03/2023, becomes 23/10/2023 → 28/10/2023.
        var plan = RebookPlanner.Plan(PeriodType.Custom, new Stay(D(2021, 10, 23), D(2021, 10, 28)), D(2023, 3, 10));
        Assert.Equal(new Stay(D(2023, 10, 23), D(2023, 10, 28)), plan.Stay);
    }

    [Fact]
    public void A_date_already_passed_this_year_goes_to_next_year()
    {
        var plan = RebookPlanner.Plan(PeriodType.Custom, new Stay(D(2025, 1, 10), D(2025, 1, 14)), D(2026, 10, 12));
        Assert.Equal(new Stay(D(2027, 1, 10), D(2027, 1, 14)), plan.Stay);
    }

    [Fact]
    public void An_upcoming_booking_is_re_booked_for_the_following_year_not_the_same_dates()
    {
        var plan = RebookPlanner.Plan(PeriodType.Custom, new Stay(D(2026, 12, 1), D(2026, 12, 4)), D(2026, 10, 12));
        Assert.Equal(new Stay(D(2027, 12, 1), D(2027, 12, 4)), plan.Stay);
    }

    [Fact]
    public void Leap_day_becomes_28_february_and_the_nights_are_kept()
    {
        var plan = RebookPlanner.Plan(PeriodType.Custom, new Stay(D(2024, 2, 29), D(2024, 3, 3)), D(2026, 1, 5));
        Assert.Equal(new Stay(D(2026, 2, 28), D(2026, 3, 3)), plan.Stay);
    }

    [Fact]
    public void Period_type_is_kept_when_the_new_dates_still_fit_its_shape()
    {
        // Mon 15/03/2021 → Fri 19/03/2021 re-booked in 2027: 15/03/2027 is also a Monday.
        var original = new Stay(D(2021, 3, 15), D(2021, 3, 19));
        var plan = RebookPlanner.Plan(PeriodType.Midweek, original, D(2027, 1, 1));
        Assert.Equal(new Stay(D(2027, 3, 15), D(2027, 3, 19)), plan.Stay);
        Assert.Equal(PeriodType.Midweek, plan.PeriodType);
    }

    [Fact]
    public void Period_type_becomes_custom_when_the_new_dates_no_longer_fit()
    {
        var original = new Stay(D(2026, 3, 16), D(2026, 3, 20)); // Mon → Fri in 2026
        var plan = RebookPlanner.Plan(PeriodType.Midweek, original, D(2026, 10, 12)); // 16/03/2027 is a Tuesday
        Assert.Equal(PeriodType.Custom, plan.PeriodType);
        Assert.Equal(new Stay(D(2027, 3, 16), D(2027, 3, 20)), plan.Stay);
    }

    [Fact]
    public void Long_weekend_is_always_kept()
    {
        var plan = RebookPlanner.Plan(PeriodType.LongWeekend, new Stay(D(2026, 6, 5), D(2026, 6, 9)), D(2026, 10, 12));
        Assert.Equal(PeriodType.LongWeekend, plan.PeriodType);
    }
}
