using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class FrequentFloaterServiceTests
{
    private readonly FrequentFloaterService service = new();
    private readonly DateTimeOffset anchor = Sydney.InstantOf(D("2026-11-09"), new TimeOnly(10, 0));

    [Theory]
    [InlineData("2025-11-09", true)]
    [InlineData("2025-11-08", false)]
    [InlineData("2027-11-09", true)]
    [InlineData("2027-11-10", false)]
    public void Other_booking_starting_within_a_year_either_side_of_the_anchor(string start, bool expected)
    {
        var other = Staff(start, D(start).AddDays(4).ToString("yyyy-MM-dd"));
        Assert.Equal(expected, service.Evaluate(anchor, [other], null).IsFrequentFloater);
    }

    [Fact]
    public void Only_other_active_non_standby_bookings_count()
    {
        var self = Staff("2027-03-08", "2027-03-12");
        var standby = Staff("2027-04-08", "2027-04-12", standby: true);
        var cancelled = Staff("2027-05-08", "2027-05-12");
        cancelled.Cancel(Now);
        var hold = Hold("2027-06-07", "2027-06-11", Now.AddMinutes(30));
        var result = service.Evaluate(anchor, [self, standby, cancelled, hold], self.Id);
        Assert.False(result.IsFrequentFloater);
        Assert.Equal(1, service.Evaluate(anchor, [self, Staff("2027-07-05", "2027-07-09")], self.Id).OtherBookingCount);
    }

    [Fact]
    public void An_existing_booking_is_anchored_at_activated_at_so_time_passing_doesnt_change_it()
    {
        var other = Staff("2026-01-05", "2026-01-09");
        var activatedAt = Sydney.InstantOf(D("2026-11-09"), new TimeOnly(10, 0));
        Assert.True(service.Evaluate(activatedAt, [other], null).IsFrequentFloater);
    }
}
