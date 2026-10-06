using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class OverlapTests
{
    private static readonly BoatId Boat = BoatId.New();

    [Fact]
    public void Same_day_changeover_does_not_overlap()
    {
        var weekend = new Stay(D(2026, 10, 16), D(2026, 10, 19));
        var midweek = new Stay(D(2026, 10, 19), D(2026, 10, 23));
        Assert.False(weekend.Overlaps(midweek));
        Assert.False(midweek.Overlaps(weekend));
    }

    [Fact]
    public void Sharing_a_night_overlaps()
    {
        var a = new Stay(D(2026, 10, 16), D(2026, 10, 19));
        var b = new Stay(D(2026, 10, 18), D(2026, 10, 20));
        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Containment_overlaps() =>
        Assert.True(new Stay(D(2026, 10, 12), D(2026, 10, 19)).Overlaps(new Stay(D(2026, 10, 13), D(2026, 10, 14))));

    [Fact]
    public void Midweek_ending_on_the_first_night_of_a_long_weekend_does_not_overlap()
    {
        // Midweek Mon 28/09 → Fri 02/10 vs long weekend with FirstNight Fri 02/10.
        var midweek = new Stay(D(2026, 9, 28), D(2026, 10, 2));
        var longWeekend = new NightRange(D(2026, 10, 2), D(2026, 10, 4));
        Assert.False(midweek.Overlaps(longWeekend));
    }

    [Fact]
    public void Midweek_with_last_night_inside_christmas_overlaps()
    {
        // Midweek Mon 17/12 → Fri 21/12 vs Christmas with FirstNight 20/12.
        var midweek = new Stay(D(2029, 12, 17), D(2029, 12, 21));
        var christmas = new NightRange(D(2029, 12, 20), D(2030, 1, 5));
        Assert.True(midweek.Overlaps(christmas));
    }

    [Fact]
    public void Stay_starting_the_morning_after_the_last_night_does_not_overlap()
    {
        var range = new NightRange(D(2026, 10, 2), D(2026, 10, 4));
        Assert.False(new Stay(D(2026, 10, 5), D(2026, 10, 9)).Overlaps(range));
        Assert.True(new Stay(D(2026, 10, 4), D(2026, 10, 9)).Overlaps(range));
    }

    [Fact]
    public void Night_range_must_be_ordered() =>
        Assert.Throws<DomainValidationException>(() => new NightRange(D(2026, 10, 5), D(2026, 10, 4)));

    [Fact]
    public void Stay_must_have_at_least_one_night() =>
        Assert.Throws<DomainValidationException>(() => new Stay(D(2026, 10, 5), D(2026, 10, 5)));

    [Fact]
    public void Active_booking_blocks_overlapping_dates()
    {
        var existing = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        var ex = Assert.Throws<AvailabilityConflictException>(() =>
            BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 10), D(2026, 12, 14)), [existing], [], Now));
        Assert.Contains(existing.Reference.Value, ex.ConflictingReferences);
        Assert.Equal(AvailabilityConflictException.DatesNoLongerAvailable, ex.Message);
    }

    [Fact]
    public void Same_day_changeover_is_available()
    {
        var existing = StaffBooking(Boat, D(2026, 12, 4), D(2026, 12, 7));
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [existing], [], Now);
    }

    [Fact]
    public void Other_boats_do_not_conflict()
    {
        var existing = StaffBooking(BoatId.New(), D(2026, 12, 7), D(2026, 12, 11));
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [existing], [], Now);
    }

    [Fact]
    public void Standby_bookings_never_block()
    {
        var standby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [standby], [], Now);
    }

    [Fact]
    public void Cancelled_bookings_never_block()
    {
        var cancelled = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        cancelled.Cancel(Now);
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [cancelled], [], Now);
    }

    [Fact]
    public void Unexpired_hold_blocks_and_expired_hold_does_not()
    {
        var hold = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        var stay = new Stay(D(2026, 12, 7), D(2026, 12, 11));
        Assert.Throws<AvailabilityConflictException>(() => BookingAvailabilityService.EnsureAvailable(Boat, stay, [hold], [], Now));
        BookingAvailabilityService.EnsureAvailable(Boat, stay, [hold], [], Now.AddMinutes(41));
    }

    [Fact]
    public void A_booking_does_not_conflict_with_itself()
    {
        var existing = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 8), D(2026, 12, 12)), [existing], [], Now, existing.Id);
    }

    [Fact]
    public void Unavailability_blocks_inclusive_nights()
    {
        var maintenance = BoatUnavailability.Create(Boat, D(2026, 12, 10), D(2026, 12, 10), "Engine", Now);
        Assert.Throws<AvailabilityConflictException>(() =>
            BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [], [maintenance], Now));
        // Checking out on the morning of 10/12 does not use the night of 10/12.
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 10)), [], [maintenance], Now);
        // Checking in on 11/12 starts after the last unavailable night.
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 11), D(2026, 12, 14)), [], [maintenance], Now);
    }

    [Fact]
    public void Unavailability_cannot_overlap_a_booking_and_names_it()
    {
        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        var ex = Assert.Throws<AvailabilityConflictException>(() =>
            BookingAvailabilityService.EnsureUnavailabilityAllowed(Boat, new NightRange(D(2026, 12, 10), D(2026, 12, 12)), [booking], Now));
        Assert.Contains(booking.Reference.Value, ex.Message);
        // Starting on the check-out day is fine.
        BookingAvailabilityService.EnsureUnavailabilityAllowed(Boat, new NightRange(D(2026, 12, 11), D(2026, 12, 12)), [booking], Now);
    }

    [Fact]
    public void Unavailability_may_overlap_a_standby()
    {
        var standby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        BookingAvailabilityService.EnsureUnavailabilityAllowed(Boat, new NightRange(D(2026, 12, 8), D(2026, 12, 9)), [standby], Now);
    }
}
