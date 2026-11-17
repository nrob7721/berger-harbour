using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class StandbyReactivationTests
{
    private static StaffBookingDetails Details(Booking b, BoatId? boat = null, string? start = null, string? end = null, bool standby = true,
        string? comments = null) =>
        new(boat ?? b.BoatId, b.PeriodType, StayRange.Create(start is null ? b.StartDate : D(start), end is null ? b.EndDate : D(end)), 2, 0,
            b.CheckInTime, b.CheckOutTime, standby, comments, b.HirePrice, b.PaymentSchedule, []);

    private static Booking Superseded()
    {
        var b = Staff("2027-03-08", "2027-03-12", standby: true);
        b.Supersede(Now);
        return b;
    }

    [Theory]
    [InlineData("misty-blue", null, null)]
    [InlineData(null, "2027-03-15", "2027-03-19")]
    [InlineData(null, null, "2027-03-13")]
    public void Moving_a_superseded_standby_to_another_boat_or_other_dates_reactivates_it_as_an_Active_standby(string? boat, string? start, string? end)
    {
        var b = Superseded();
        Assert.True(b.UpdateByStaff(Details(b, boat is null ? null : new BoatId(boat), start, end), Now));
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.True(b.IsStandby);
    }

    [Fact]
    public void A_save_that_changes_neither_the_boat_nor_the_dates_keeps_it_Superseded()
    {
        var b = Superseded();
        Assert.False(b.UpdateByStaff(Details(b, comments: "Called back"), Now));
        Assert.Equal(BookingStatus.Superseded, b.Status);
        Assert.Equal("Called back", b.BookingComments);
    }

    [Fact]
    public void A_superseded_booking_keeps_its_standby_flag_and_status()
    {
        var b = Superseded();
        var e = Assert.Throws<DomainValidationException>(() => b.UpdateByStaff(Details(b, start: "2027-03-15", end: "2027-03-19", standby: false), Now));
        Assert.True(e.Errors.Has("isStandby"));
        Assert.Equal(BookingStatus.Superseded, b.Status);
        Assert.Throws<DomainValidationException>(() => b.ChangeStatusByStaff(BookingStatus.Active, Now));
        Assert.Throws<DomainValidationException>(() => b.ChangeStatusByStaff(BookingStatus.Cancelled, Now));
    }

    [Fact]
    public void Once_reactivated_it_can_be_superseded_again()
    {
        var b = Superseded();
        b.UpdateByStaff(Details(b, start: "2027-03-15", end: "2027-03-19"), Now);
        b.Supersede(Now);
        Assert.Equal(BookingStatus.Superseded, b.Status);
    }
}
