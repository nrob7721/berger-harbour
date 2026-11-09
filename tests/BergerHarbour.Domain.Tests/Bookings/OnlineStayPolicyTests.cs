using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class OnlineStayPolicyTests
{
    private static readonly DateOnly Today = D("2026-11-09");
    private readonly OnlineStayPolicy policy = new();
    private readonly BusinessSettings settings = BusinessSettings.Create(1000, 30, D("2027-11-30"), null, "02 4455 6677", "info@example.com", null, null,
        null, null);

    [Fact]
    public void Exactly_the_lead_time_is_allowed() =>
        Assert.True(policy.Check(PeriodType.Midweek, D("2026-12-14"), settings, [], D("2026-11-14")).IsValid);

    [Fact]
    public void One_day_inside_the_lead_time_is_rejected()
    {
        var check = policy.Check(PeriodType.Midweek, D("2026-12-14"), settings, [], D("2026-11-15"));
        Assert.True(check.Errors.Has("startDate"));
    }

    [Fact]
    public void Last_night_on_open_until_is_allowed()
    {
        var s = BusinessSettings.Create(1000, 30, D("2027-03-11"), null, null, null, null, null, null, null);
        Assert.True(policy.Check(PeriodType.Midweek, D("2027-03-08"), s, [], Today).IsValid);
    }

    [Fact]
    public void Last_night_after_open_until_is_rejected()
    {
        var s = BusinessSettings.Create(1000, 30, D("2027-03-10"), null, null, null, null, null, null, null);
        Assert.False(policy.Check(PeriodType.Midweek, D("2027-03-08"), s, [], Today).IsValid);
    }

    [Fact]
    public void A_stay_overlapping_a_blocked_period_is_rejected_with_the_contact_details()
    {
        var christmas = BlockedPeriod.Create(BlockedPeriodId.New(), "Christmas", NightRange.Create(D("2026-12-20"), D("2027-01-05")), true);
        var check = policy.Check(PeriodType.Midweek, D("2026-12-14"), settings, [christmas], D("2026-11-01"));
        Assert.True(check.IsValid);
        var clash = policy.Check(PeriodType.Weekend, D("2026-12-18"), settings, [christmas], D("2026-11-01"));
        Assert.Contains("Bookings over Christmas must be made by phone or email: 02 4455 6677 / info@example.com.", clash.Errors.Messages);
    }

    [Fact]
    public void Wrong_shape_reports_only_the_shape()
    {
        var check = policy.Check(PeriodType.Midweek, D("2027-03-09"), settings, [], Today);
        Assert.Null(check.Stay);
        Assert.Contains(StayPeriodFactory.InvalidShape, check.Errors.Messages);
    }
}
