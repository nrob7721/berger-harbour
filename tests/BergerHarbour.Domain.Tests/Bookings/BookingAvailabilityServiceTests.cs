using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class BookingAvailabilityServiceTests
{
    private readonly BookingAvailabilityService service = new();

    private static StayRange S(string start, string end) => StayRange.Create(D(start), D(end));

    [Fact]
    public void Same_day_changeover_is_available() =>
        Assert.True(service.IsFree(S("2027-03-12", "2027-03-15"), [Staff("2027-03-08", "2027-03-12")], [], Now));

    [Fact]
    public void An_overlapping_active_booking_conflicts_and_names_its_reference()
    {
        var e = Assert.Throws<ConflictException>(() => service.EnsureAvailable(S("2027-01-17", "2027-01-20"), [Staff("2027-01-15", "2027-01-19")], [], Now));
        Assert.Equal(ConflictException.DatesTaken, e.Message);
        Assert.Equal(["SMITH-2701150"], e.ConflictingReferences);
    }

    [Fact]
    public void Standbys_cancelled_expired_superseded_and_expired_holds_never_block()
    {
        var cancelled = Staff("2027-03-08", "2027-03-12");
        cancelled.Cancel(Now);
        var expiredHold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(-1));
        var expired = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(30));
        expired.Expire(Now);
        var superseded = Staff("2027-03-08", "2027-03-12", standby: true);
        superseded.Supersede(Now);
        Assert.True(service.IsFree(S("2027-03-08", "2027-03-12"),
            [Staff("2027-03-08", "2027-03-12", standby: true), cancelled, expiredHold, expired, superseded], [], Now));
    }

    [Fact]
    public void A_live_hold_blocks() => Assert.False(service.IsFree(S("2027-03-08", "2027-03-12"), [Hold("2027-03-08", "2027-03-12", Now.AddMinutes(5))], [], Now));

    [Fact]
    public void Unavailability_uses_the_inclusive_night_rule()
    {
        var maintenance = BoatUnavailability.Create(BoatUnavailabilityId.New(), PacificBlue, NightRange.Create(D("2027-03-12"), D("2027-03-14")), null, Now);
        Assert.True(service.IsFree(S("2027-03-08", "2027-03-12"), [], [maintenance], Now));
        Assert.False(service.IsFree(S("2027-03-14", "2027-03-16"), [], [maintenance], Now));
        Assert.True(service.IsFree(S("2027-03-15", "2027-03-16"), [], [maintenance], Now));
    }

    [Fact]
    public void An_active_non_standby_supersedes_overlapping_active_standbys_of_the_same_boat_only()
    {
        var standby = Staff("2027-03-09", "2027-03-11", standby: true);
        var otherBoat = Staff("2027-03-09", "2027-03-11", standby: true, boat: MistyBlue);
        var notOverlapping = Staff("2027-03-12", "2027-03-14", standby: true);
        var booking = Staff("2027-03-08", "2027-03-12");
        Assert.Equal([standby], service.StandbysSupersededBy(booking, [standby, otherBoat, notOverlapping, booking]));
        Assert.Empty(service.StandbysSupersededBy(Staff("2027-03-08", "2027-03-12", standby: true), [standby]));
    }

    [Fact]
    public void An_unavailability_may_not_overlap_an_active_booking()
    {
        var e = Assert.Throws<ConflictException>(() =>
            service.EnsureUnavailabilityAllowed(NightRange.Create(D("2027-01-18"), D("2027-01-20")), [Staff("2027-01-15", "2027-01-19")], Now));
        Assert.Contains("SMITH-2701150", e.Message);
        service.EnsureUnavailabilityAllowed(NightRange.Create(D("2027-01-19"), D("2027-01-20")), [Staff("2027-01-15", "2027-01-19")], Now);
    }
}
