using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

public class StaffBookingWarningsTests
{
    private static readonly AddonDefinition Parking =
        AddonDefinition.Create(new AddonId("car-parking"), "Car parking", null, true, AddonPrices.None, true, 11, "car spots", 0);

    private static readonly Boat Boat = Boat.Create(new BoatId("pacific-blue"), new BoatDetails("Pacific Blue", "pacific-blue", BoatType.House, 12, 6, 2,
        "4 queens", 2000, null, null, false, false, [], []), Start);

    private readonly StaffBookingWarnings warnings = new(new AddonCapacityService());

    [Fact]
    public void Guests_above_berth_and_above_beds()
    {
        var result = warnings.For(Boat, Booking("2027-03-08", "2027-03-12", adults: 13), [], [], [], Start);
        Assert.Contains("13 guests is more than Pacific Blue's berth of 12.", result);
        Assert.Contains(result, w => w.StartsWith("13 guests is more than Pacific Blue's 8 beds", StringComparison.Ordinal));
        Assert.Empty(warnings.For(Boat, Booking("2027-03-08", "2027-03-12", adults: 8), [], [], [], Start));
    }

    [Fact]
    public void Addon_capacity_exceeded_is_a_warning_not_an_error()
    {
        var other = Booking("2026-10-12", "2026-10-15", lines: [AddonLine.Create(null, Parking.Id, "Car parking", 10, null)]);
        var mine = Booking("2026-10-14", "2026-10-16", lines: [AddonLine.Create(null, Parking.Id, "Car parking", 2, null)]);
        Assert.Equal(["This exceeds car parking capacity on 14/10/2026."], warnings.For(Boat, mine, [Parking], [], [other, mine], Start));
    }

    [Fact]
    public void A_custom_time_clashing_with_a_same_day_changeover()
    {
        var before = Booking("2027-03-05", "2027-03-08", checkOut: new TimeOnly(23, 0));
        var after = Booking("2027-03-12", "2027-03-15");
        var mine = Booking("2027-03-08", "2027-03-12", checkOut: new TimeOnly(12, 0));
        var result = warnings.For(Boat, mine, [], [before, after], [], Start);
        Assert.Equal(2, result.Count);
        Assert.Contains("Check-in at 11am on 08/03/2027 is before SMITH-2703050's check-out at 11pm.", result);
        Assert.Contains("Check-out at 12pm on 12/03/2027 is after SMITH-2703120's check-in at 11am.", result);
        Assert.Empty(warnings.For(Boat, Booking("2027-03-08", "2027-03-12"), [], [Booking("2027-03-05", "2027-03-08")], [], Start));
    }

    private static Booking Booking(string start, string end, int adults = 2, IReadOnlyList<AddonLine>? lines = null, TimeOnly? checkOut = null) =>
        Domain.Bookings.Booking.CreateByStaff(BookingId.New(), BookingReference.Create(BookingReference.PrefixFor("Smith", D(start)), 0), new CustomerId("c"),
            new StaffBookingDetails(Boat.Id, PeriodType.Custom, StayRange.Create(D(start), D(end)), adults, 0, Sydney.StandardCheckIn,
                checkOut ?? Sydney.StandardCheckOut, false, null, 1000, PaymentScheduleType.Standard, lines ?? []), 1000, "h", Start);
}
