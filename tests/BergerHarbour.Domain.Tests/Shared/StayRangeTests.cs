using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Shared;

public class StayRangeTests
{
    private static StayRange S(string start, string end) => StayRange.Create(D(start), D(end));

    private static NightRange N(string first, string last) => NightRange.Create(D(first), D(last));

    [Fact]
    public void Same_day_changeover_does_not_overlap() => Assert.False(S("2026-10-09", "2026-10-12").Overlaps(S("2026-10-12", "2026-10-16")));

    [Fact]
    public void Overlapping_nights_overlap() => Assert.True(S("2026-10-09", "2026-10-13").Overlaps(S("2026-10-12", "2026-10-16")));

    [Fact]
    public void Midweek_ending_on_a_long_weekends_first_night_does_not_overlap_it() =>
        Assert.False(S("2026-09-28", "2026-10-02").Overlaps(N("2026-10-02", "2026-10-04")));

    [Fact]
    public void Midweek_including_christmas_first_night_overlaps_it() =>
        Assert.True(S("2026-12-17", "2026-12-21").Overlaps(N("2026-12-20", "2027-01-05")));

    [Fact]
    public void Stay_starting_on_the_last_night_overlaps() => Assert.True(S("2027-01-05", "2027-01-08").Overlaps(N("2026-12-20", "2027-01-05")));

    [Fact]
    public void Stay_starting_the_morning_after_the_last_night_does_not_overlap() =>
        Assert.False(S("2027-01-06", "2027-01-08").Overlaps(N("2026-12-20", "2027-01-05")));

    [Fact]
    public void End_must_be_after_start() => Assert.Throws<DomainValidationException>(() => S("2026-10-12", "2026-10-12"));

    [Fact]
    public void Night_range_last_may_not_be_before_first() => Assert.Throws<DomainValidationException>(() => N("2026-10-12", "2026-10-11"));

    [Fact]
    public void Nights_are_start_to_end_minus_one() =>
        Assert.Equal([D("2026-10-12"), D("2026-10-13")], S("2026-10-12", "2026-10-14").EachNight());
}
