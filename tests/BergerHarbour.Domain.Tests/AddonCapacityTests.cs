using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class AddonCapacityTests
{
    private static readonly AddonDefinition CarParking =
        AddonDefinition.Create("Car parking", null, true, null, true, 11, "car spots", 5);

    private static Booking WithParking(BoatId boat, DateOnly start, DateOnly end, int spots, bool standby = false)
    {
        var booking = StaffBooking(boat, start, end, standby);
        booking.AddAddon(new RequestedAddon(CarParking.Id, "Car parking", spots, null), Now);
        return booking;
    }

    /// <summary>§7.5: Pacific Blue 12/10 → 15/10 with 3 spots, Misty Blue 14/10 → 19/10 with 2 spots.</summary>
    private static List<Booking> WorkedExample() =>
    [
        WithParking(BoatId.New(), D(2026, 10, 12), D(2026, 10, 15), 3),
        WithParking(BoatId.New(), D(2026, 10, 14), D(2026, 10, 19), 2),
    ];

    [Theory]
    [InlineData(12, 15, 6)]
    [InlineData(12, 14, 8)]
    [InlineData(14, 16, 6)]
    [InlineData(15, 18, 9)]
    public void Remaining_is_measured_on_the_busiest_night_across_all_boats(int startDay, int endDay, int expected) =>
        Assert.Equal(expected, AddonCapacityService.Remaining(CarParking, new Stay(D(2026, 10, startDay), D(2026, 10, endDay)),
            WorkedExample(), Now));

    [Fact]
    public void Used_on_a_night_counts_stays_that_include_that_night()
    {
        var bookings = WorkedExample();
        Assert.Equal(5, AddonCapacityService.UsedOnNight(CarParking.Id, D(2026, 10, 14), bookings, Now));
        Assert.Equal(2, AddonCapacityService.UsedOnNight(CarParking.Id, D(2026, 10, 15), bookings, Now)); // check-out day
    }

    [Fact]
    public void Stand_bys_cancelled_expired_superseded_and_lapsed_holds_do_not_count()
    {
        var boat = BoatId.New();
        var stay = new Stay(D(2026, 10, 12), D(2026, 10, 15));
        var standby = WithParking(boat, stay.StartDate, stay.EndDate, 4, standby: true);
        var superseded = WithParking(boat, stay.StartDate, stay.EndDate, 4, standby: true);
        superseded.Supersede(Now);
        var cancelled = WithParking(boat, stay.StartDate, stay.EndDate, 4);
        cancelled.Cancel(Now);
        var expired = OnlineHold(boat, stay.StartDate, stay.EndDate, Now.AddMinutes(40),
            addons: [new RequestedAddon(CarParking.Id, "Car parking", 4, null)]);
        expired.Expire(Now);
        var lapsed = OnlineHold(boat, stay.StartDate, stay.EndDate, Now.AddMinutes(40),
            addons: [new RequestedAddon(CarParking.Id, "Car parking", 4, null)]);
        var live = OnlineHold(boat, stay.StartDate, stay.EndDate, Now.AddMinutes(40),
            addons: [new RequestedAddon(CarParking.Id, "Car parking", 1, null)]);

        Assert.Equal(11, AddonCapacityService.Remaining(CarParking, stay, [standby, superseded, cancelled, expired], Now));
        Assert.Equal(11, AddonCapacityService.Remaining(CarParking, stay, [lapsed], Now.AddHours(1)));
        Assert.Equal(10, AddonCapacityService.Remaining(CarParking, stay, [live], Now));
    }

    [Fact]
    public void A_booking_being_edited_is_excluded_from_its_own_count()
    {
        var bookings = WorkedExample();
        var stay = new Stay(D(2026, 10, 12), D(2026, 10, 15));
        Assert.Equal(6, AddonCapacityService.Remaining(CarParking, stay, bookings, Now));
        Assert.Equal(9, AddonCapacityService.Remaining(CarParking, stay, bookings, Now, bookings[0].Id));
    }

    [Fact]
    public void Remaining_never_goes_below_zero()
    {
        var full = WithParking(BoatId.New(), D(2026, 10, 12), D(2026, 10, 15), 12);
        Assert.Equal(0, AddonCapacityService.Remaining(CarParking, new Stay(D(2026, 10, 12), D(2026, 10, 13)), [full], Now));
    }

    [Fact]
    public void First_night_exceeded_names_the_night_for_the_staff_warning()
    {
        var bookings = WorkedExample();
        var stay = new Stay(D(2026, 10, 12), D(2026, 10, 15));
        Assert.Null(AddonCapacityService.FirstNightExceeded(CarParking, stay, 6, bookings, Now));
        Assert.Equal(D(2026, 10, 14), AddonCapacityService.FirstNightExceeded(CarParking, stay, 7, bookings, Now));
    }
}
