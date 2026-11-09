using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class StatusTransitionTests
{
    [Fact]
    public void Hold_activates_with_reference_and_activation_instant()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Null(hold.Reference);
        hold.ActivateAfterDeposit("SMITH-2703080", Now.AddMinutes(5));
        Assert.Equal(BookingStatus.Active, hold.Status);
        Assert.Equal("SMITH-2703080", hold.Reference);
        Assert.Equal(Now.AddMinutes(5), hold.ActivatedAt);
        Assert.Null(hold.HoldExpiresAt);
    }

    [Fact]
    public void Hold_expires_idempotently()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        hold.Expire();
        hold.Expire();
        Assert.Equal(BookingStatus.Expired, hold.Status);
        Assert.Null(hold.Reference);
    }

    [Fact]
    public void Active_booking_cannot_expire()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12");
        Assert.Throws<ConflictException>(b.Expire);
    }

    [Fact]
    public void Cancel_and_reactivate()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12");
        b.Cancel();
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Throws<ConflictException>(b.Cancel);
        var activatedAt = b.ActivatedAt;
        b.Reactivate();
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal(activatedAt, b.ActivatedAt); // unchanged by Cancelled → Active
        Assert.Throws<ConflictException>(b.Reactivate);
    }

    [Fact]
    public void Illegal_transitions_are_rejected()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Throws<ConflictException>(hold.Cancel);
        Assert.Throws<ConflictException>(hold.Supersede);
        var staff = StaffBooking("2027-03-08", "2027-03-12");
        Assert.Throws<ConflictException>(() => staff.ActivateAfterDeposit("X-1", Now));
        Assert.Throws<ConflictException>(staff.Supersede); // not a stand-by
    }

    [Fact]
    public void Staff_booking_starts_active_with_reference_and_activation()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12");
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.NotNull(b.Reference);
        Assert.Equal(Now, b.ActivatedAt);
        Assert.Equal(BookingCreator.SystemUser, b.CreatedBy);
    }

    [Fact]
    public void Pending_payment_bookings_cannot_be_edited_by_staff()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Throws<ConflictException>(() => hold.UpdateByStaff(hold.BoatId, PeriodType.Midweek, hold.Stay, hold.Guests,
            hold.CheckInTime, hold.CheckOutTime, false, null, 1, PaymentScheduleType.Standard));
    }
}

public class StandbyTests
{
    [Fact]
    public void Active_non_standby_supersedes_overlapping_active_standbys_on_the_same_boat()
    {
        var standby = StaffBooking("2027-03-08", "2027-03-12", standby: true);
        var otherBoat = StaffBooking("2027-03-08", "2027-03-12", boat: new BoatId("boat-2"), standby: true);
        var later = StaffBooking("2027-03-12", "2027-03-15", standby: true);
        var active = StaffBooking("2027-03-09", "2027-03-12");
        var result = BookingAvailabilityService.StandbysToSupersede(active, [standby, otherBoat, later]);
        Assert.Equal([standby.Id], result.Select(b => b.Id));
        standby.Supersede();
        Assert.Equal(BookingStatus.Superseded, standby.Status);
    }

    [Fact]
    public void Standbys_never_block_and_ignore_other_standbys()
    {
        var standby = StaffBooking("2027-03-08", "2027-03-12", standby: true);
        Assert.False(standby.BlocksAvailability(Now));
        Assert.Empty(BookingAvailabilityService.StandbysToSupersede(StaffBooking("2027-03-08", "2027-03-12", standby: true), [standby]));
    }
}

public class GuestRuleTests
{
    [Fact]
    public void At_least_one_adult_and_no_negative_children()
    {
        Assert.Throws<DomainValidationException>(() => new GuestNumbers(0, 2));
        Assert.Throws<DomainValidationException>(() => new GuestNumbers(2, -1));
        Assert.Equal(5, new GuestNumbers(3, 2).Total);
    }

    [Fact]
    public void Rooming_warning_applies_above_adult_plus_kids_beds()
    {
        var boat = Boat(); // 6 adult + 2 kids beds
        Assert.False(RoomingWarning.Applies(boat, 8));
        Assert.True(RoomingWarning.Applies(boat, 9));
        Assert.Contains("With 9 guests", RoomingWarning.Text(boat, 9));
        Assert.Contains("6 adult beds and 2 children's beds", RoomingWarning.Text(boat, 9));
    }
}

public class AddonCapacityTests
{
    // Car parking, capacity 11: Pacific Blue 12/10 → 15/10 with 3 spots, Misty Blue 14/10 → 19/10 with 2 spots.
    private static IReadOnlyCollection<Booking> Existing() =>
    [
        StaffBooking("2026-10-12", "2026-10-15", boat: new BoatId("pacific-blue"), lines: [Line("car", 3)]),
        StaffBooking("2026-10-14", "2026-10-19", boat: new BoatId("misty-blue"), lines: [Line("car", 2)]),
    ];

    [Theory]
    [InlineData("2026-10-12", "2026-10-15", 6)]
    [InlineData("2026-10-12", "2026-10-14", 8)]
    [InlineData("2026-10-14", "2026-10-16", 6)]
    [InlineData("2026-10-15", "2026-10-18", 9)]
    public void Remaining_is_measured_on_the_busiest_night(string start, string end, int remaining) =>
        Assert.Equal(remaining, AddonCapacityService.Remaining(11, "car", Stay(start, end), Existing(), Now));

    [Fact]
    public void Standby_cancelled_expired_superseded_and_lapsed_holds_do_not_count()
    {
        var cancelled = StaffBooking("2026-10-12", "2026-10-15", lines: [Line("car", 5)]);
        cancelled.Cancel();
        var superseded = StaffBooking("2026-10-12", "2026-10-15", standby: true, lines: [Line("car", 5)]);
        superseded.Supersede();
        var expired = Hold("2026-10-12", "2026-10-15", Now.AddMinutes(30), lines: [Line("car", 5)]);
        expired.Expire();
        var bookings = new[]
        {
            cancelled, superseded, expired,
            StaffBooking("2026-10-12", "2026-10-15", standby: true, lines: [Line("car", 5)]),
            Hold("2026-10-12", "2026-10-15", Now.AddMinutes(-1), lines: [Line("car", 5)]),
        };
        Assert.Equal(11, AddonCapacityService.Remaining(11, "car", Stay("2026-10-12", "2026-10-15"), bookings, Now));
        var live = Hold("2026-10-12", "2026-10-15", Now.AddMinutes(10), lines: [Line("car", 4)]);
        Assert.Equal(7, AddonCapacityService.Remaining(11, "car", Stay("2026-10-12", "2026-10-15"), [.. bookings, live], Now));
    }

    [Fact]
    public void The_booking_being_edited_is_excluded()
    {
        var existing = Existing();
        var first = existing.First();
        Assert.Equal(9, AddonCapacityService.Remaining(11, "car", Stay("2026-10-12", "2026-10-15"), existing, Now, first.Id));
    }

    [Fact]
    public void First_night_exceeded_is_reported()
    {
        Assert.Equal(D("2026-10-14"), AddonCapacityService.FirstNightExceeded(11, "car", Stay("2026-10-12", "2026-10-16"), 7, Existing(), Now));
        Assert.Null(AddonCapacityService.FirstNightExceeded(11, "car", Stay("2026-10-12", "2026-10-16"), 6, Existing(), Now));
    }
}

public class BookingReferenceTests
{
    [Fact]
    public void Format_is_lastname_then_start_date_then_suffix() =>
        Assert.Equal("SMITH-2701150", BookingReference.Compose(BookingReference.Prefix("Smith", D("2027-01-15")), 0));

    [Theory]
    [InlineData("Müller", "MULLER")]
    [InlineData("O'Brien", "OBRIEN")]
    [InlineData("Smith-Jones", "SMITHJONES")]
    [InlineData("van der Berg", "VANDERBERG")]
    [InlineData("Wolfeschlegelsteinhausenbergerdorff", "WOLFESCHLEGELSTEINHA")]
    [InlineData("王", "GUEST")]
    [InlineData("", "GUEST")]
    public void Name_part_is_cleaned_up(string lastName, string expected) => Assert.Equal(expected, BookingReference.NamePart(lastName));

    [Fact]
    public void Suffix_continues_past_nine()
    {
        Assert.Equal("SMITH-2701159", BookingReference.Compose("SMITH-270115", 9));
        Assert.Equal("SMITH-27011510", BookingReference.Compose("SMITH-270115", 10));
        Assert.Equal("SMITH-27011511", BookingReference.Compose("SMITH-270115", 11));
    }

    [Fact]
    public void Reference_never_changes_after_dates_or_name_change()
    {
        var b = StaffBooking("2027-01-15", "2027-01-18");
        var reference = b.Reference;
        b.UpdateByStaff(b.BoatId, PeriodType.Custom, Stay("2027-02-01", "2027-02-05"), b.Guests, b.CheckInTime, b.CheckOutTime, false, null, 1,
            PaymentScheduleType.Standard);
        b.ChangeCustomer("someone-else");
        b.AssignReference("OTHER-2702010");
        Assert.Equal(reference, b.Reference);
    }
}

public class FrequentFloaterTests
{
    private static readonly DateTimeOffset Anchor = SydneyTime.ToInstant(D("2027-01-15"), new TimeOnly(10, 0));

    [Theory]
    [InlineData("2026-01-15", true)] // anchor − 1 year
    [InlineData("2026-01-14", false)]
    [InlineData("2028-01-15", true)] // anchor + 1 year
    [InlineData("2028-01-16", false)]
    public void Window_is_one_year_either_side_of_the_anchor(string otherStart, bool expected)
    {
        var other = StaffBooking(otherStart, D(otherStart).AddDays(3).ToString("yyyy-MM-dd"));
        Assert.Equal(expected, FrequentFloaterService.Evaluate(Anchor, "this", [other]).IsFrequentFloater);
    }

    [Fact]
    public void Only_other_active_non_standby_bookings_count()
    {
        var self = StaffBooking("2027-01-15", "2027-01-18");
        var cancelled = StaffBooking("2027-03-01", "2027-03-04");
        cancelled.Cancel();
        var standby = StaffBooking("2027-03-01", "2027-03-04", standby: true);
        var hold = Hold("2027-03-01", "2027-03-04", Now.AddMinutes(30));
        Assert.False(FrequentFloaterService.Evaluate(Anchor, self.Id, [self, cancelled, standby, hold]).IsFrequentFloater);
        var result = FrequentFloaterService.Evaluate(Anchor, self.Id, [self, StaffBooking("2027-06-01", "2027-06-04")]);
        Assert.Equal(new FrequentFloaterResult(true, 1), result);
    }

    [Fact]
    public void Existing_booking_is_anchored_at_its_activation()
    {
        var created = SydneyTime.ToInstant(D("2026-01-01"), new TimeOnly(9, 0));
        var booking = StaffBooking("2026-03-01", "2026-03-04", createdAt: created);
        var other = StaffBooking("2026-12-30", "2027-01-02");
        // A year later "now" would move the window, but the anchor is fixed at ActivatedAt.
        Assert.True(FrequentFloaterService.Evaluate(booking.ActivatedAt!.Value, booking.Id, [booking, other]).IsFrequentFloater);
        Assert.Equal("⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one.",
            FrequentFloaterService.Notice("Jane Smith", new FrequentFloaterResult(true, 1)));
    }
}

public class BookingDraftTests
{
    private static DraftDetails Details(DraftStep step = DraftStep.Details) => new(new BoatId("boat-1"), PeriodType.Midweek, Stay("2027-03-08", "2027-03-12"),
        new GuestNumbers(2, 0), "Jane", "Smith", " Jane@Example.com ", "0412 345 678", [], step);

    [Fact]
    public void Draft_normalises_email_and_mobile_and_has_an_unguessable_id()
    {
        var d = BookingDraft.Create(Details(), Now);
        Assert.Equal("jane@example.com", d.Email);
        Assert.Equal("+61412345678", d.MobileNumber);
        Assert.Equal(32, d.Id.Length);
    }

    [Fact]
    public void Draft_is_abandoned_after_inactivity_unless_converted()
    {
        var d = BookingDraft.Create(Details(), Now);
        var after = TimeSpan.FromHours(2);
        Assert.False(d.IsAbandoned(Now.AddHours(2), after));
        Assert.True(d.IsAbandoned(Now.AddHours(2).AddSeconds(1), after));
        d.Update(Details(DraftStep.Summary), Now.AddHours(1));
        Assert.False(d.IsAbandoned(Now.AddHours(2).AddSeconds(1), after));
        d.MarkConverted("b1", Now.AddHours(1));
        Assert.False(d.IsAbandoned(Now.AddDays(1), after));
    }

    [Fact]
    public void Converted_draft_cannot_be_updated_and_retention_runs_from_creation()
    {
        var d = BookingDraft.Create(Details(), Now);
        d.MarkConverted("b1", Now);
        Assert.Throws<NotFoundException>(() => d.Update(Details(), Now));
        Assert.True(d.IsPastRetention(Now.AddDays(30), TimeSpan.FromDays(30)));
        Assert.False(d.IsPastRetention(Now.AddDays(29), TimeSpan.FromDays(30)));
    }

    [Fact]
    public void Last_step_reached_never_goes_backwards()
    {
        var d = BookingDraft.Create(Details(DraftStep.Details), Now);
        d.Update(Details(DraftStep.Summary), Now);
        d.Update(Details(DraftStep.Addons), Now);
        Assert.Equal(DraftStep.Summary, d.LastStepReached);
    }
}
