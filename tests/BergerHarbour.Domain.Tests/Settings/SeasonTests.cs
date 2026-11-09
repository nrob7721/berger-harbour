using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Settings;

public class SeasonTests
{
    private static SeasonRange R(int d1, int m1, int d2, int m2) => new(DayMonth.Create(d1, m1), DayMonth.Create(d2, m2));

    [Fact]
    public void Overlapping_ranges_of_different_seasons_are_rejected()
    {
        var peak = Season.Create(SeasonId.New(), "Peak", [R(1, 12, 31, 1)], []);
        var e = Assert.Throws<DomainValidationException>(() => Season.Create(SeasonId.New(), "Summer", [R(15, 1, 28, 2)], [peak]));
        Assert.True(e.Errors.Has("ranges"));
    }

    [Fact]
    public void Adjacent_ranges_are_allowed() =>
        Season.Create(SeasonId.New(), "Late summer", [R(1, 2, 28, 2)], [Season.Create(SeasonId.New(), "Peak", [R(1, 12, 31, 1)], [])]);

    [Fact]
    public void The_default_season_cannot_be_deleted() =>
        Assert.Throws<BusinessRuleException>(() => Season.CreateDefault(SeasonId.New()).EnsureCanDelete());

    [Theory]
    [InlineData("31-02")]
    [InlineData("1-12")]
    [InlineData("00-01")]
    public void Invalid_day_months_are_rejected(string text) => Assert.Throws<DomainValidationException>(() => DayMonth.Parse(text));

    [Fact]
    public void Leap_day_is_a_valid_day_month() => Assert.Equal("29-02", DayMonth.Parse("29-02").ToString());
}
