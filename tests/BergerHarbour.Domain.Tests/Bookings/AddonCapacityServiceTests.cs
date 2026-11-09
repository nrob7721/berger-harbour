using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class AddonCapacityServiceTests
{
    private static readonly AddonDefinition CarParking =
        AddonDefinition.Create(new AddonId("car-parking"), "Car parking", null, true, AddonPrices.None, true, 11, "car spots", 0);

    private readonly AddonCapacityService service = new();

    // Pacific Blue 12/10 → 15/10 with 3 spots, Misty Blue 14/10 → 19/10 with 2 spots.
    private static List<Booking> Existing() =>
    [
        Staff("2026-10-12", "2026-10-15", lines: [Line("car-parking", 3)]),
        Staff("2026-10-14", "2026-10-19", boat: MistyBlue, lines: [Line("car-parking", 2)]),
    ];

    [Theory]
    [InlineData("2026-10-12", "2026-10-15", 6)]
    [InlineData("2026-10-12", "2026-10-14", 8)]
    [InlineData("2026-10-14", "2026-10-16", 6)]
    [InlineData("2026-10-15", "2026-10-18", 9)]
    public void Remaining_is_measured_on_the_busiest_night_across_all_boats(string start, string end, int remaining) =>
        Assert.Equal(remaining, service.Remaining(CarParking, StayRange.Create(D(start), D(end)), Existing(), Now));

    [Fact]
    public void Only_bookings_that_block_availability_count()
    {
        var cancelled = Staff("2026-10-12", "2026-10-15", lines: [Line("car-parking", 5)]);
        cancelled.Cancel(Now);
        var expiredHold = Hold("2026-10-12", "2026-10-15", Now.AddMinutes(-1), lines: [Line("car-parking", 5)]);
        var expired = Hold("2026-10-12", "2026-10-15", Now.AddMinutes(10), lines: [Line("car-parking", 5)]);
        expired.Expire(Now);
        var superseded = Staff("2026-10-12", "2026-10-15", standby: true, lines: [Line("car-parking", 5)]);
        superseded.Supersede(Now);
        var standby = Staff("2026-10-12", "2026-10-15", standby: true, lines: [Line("car-parking", 5)]);
        var liveHold = Hold("2026-10-12", "2026-10-15", Now.AddMinutes(10), lines: [Line("car-parking", 1)]);
        Assert.Equal(10, service.Remaining(CarParking, StayRange.Create(D("2026-10-12"), D("2026-10-15")),
            [cancelled, expiredHold, expired, superseded, standby, liveHold], Now));
    }

    [Fact]
    public void The_booking_being_edited_is_excluded()
    {
        var existing = Existing();
        Assert.Equal(9, service.Remaining(CarParking, StayRange.Create(D("2026-10-12"), D("2026-10-15")), existing, Now, existing[0].Id));
    }

    [Fact]
    public void Nights_over_capacity_are_reported_for_staff_warnings()
    {
        var nights = service.NightsOverCapacity(CarParking, StayRange.Create(D("2026-10-13"), D("2026-10-16")), 7, Existing(), Now);
        Assert.Equal([D("2026-10-14")], nights);
    }

    [Fact]
    public void Remaining_never_goes_below_zero() =>
        Assert.Equal(0, service.Remaining(CarParking, StayRange.Create(D("2026-10-12"), D("2026-10-13")),
            [Staff("2026-10-12", "2026-10-13", lines: [Line("car-parking", 12)])], Now));
}
