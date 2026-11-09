using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class StayPeriodFactoryTests
{
    // Week of Mon 12/10/2026 … Sun 18/10/2026, plus Mon 19/10.
    [Theory]
    [InlineData("2026-10-12", "2026-10-12", "2026-10-16")] // Mon
    [InlineData("2026-10-13", "2026-10-12", "2026-10-16")] // Tue
    [InlineData("2026-10-14", "2026-10-12", "2026-10-16")] // Wed
    [InlineData("2026-10-15", "2026-10-12", "2026-10-16")] // Thu
    [InlineData("2026-10-16", "2026-10-12", "2026-10-16")] // Fri: the Mon→Fri ending that day
    [InlineData("2026-10-17", null, null)] // Sat
    [InlineData("2026-10-18", null, null)] // Sun
    public void Midweek_click_selects_the_monday_to_friday_containing_the_day(string clicked, string? start, string? end) =>
        AssertClick(PeriodType.Midweek, clicked, start, end);

    [Theory]
    [InlineData("2026-10-12", "2026-10-09", "2026-10-12")] // Mon: the weekend ending that day
    [InlineData("2026-10-13", null, null)] // Tue
    [InlineData("2026-10-14", null, null)] // Wed
    [InlineData("2026-10-15", null, null)] // Thu
    [InlineData("2026-10-16", "2026-10-16", "2026-10-19")] // Fri
    [InlineData("2026-10-17", "2026-10-16", "2026-10-19")] // Sat
    [InlineData("2026-10-18", "2026-10-16", "2026-10-19")] // Sun
    public void Weekend_click_selects_the_friday_to_monday_containing_the_day(string clicked, string? start, string? end) =>
        AssertClick(PeriodType.Weekend, clicked, start, end);

    [Theory]
    [InlineData("2026-10-12", "2026-10-12", "2026-10-19")] // Mon → Mon
    [InlineData("2026-10-13", null, null)]
    [InlineData("2026-10-14", null, null)]
    [InlineData("2026-10-15", null, null)]
    [InlineData("2026-10-16", "2026-10-16", "2026-10-23")] // Fri → Fri
    [InlineData("2026-10-17", null, null)]
    [InlineData("2026-10-18", null, null)]
    public void Week_click_starts_on_the_clicked_monday_or_friday(string clicked, string? start, string? end) =>
        AssertClick(PeriodType.Week, clicked, start, end);

    private static void AssertClick(PeriodType type, string clicked, string? start, string? end)
    {
        var stay = StayPeriodFactory.ForClick(type, D(clicked));
        if (start is null) Assert.Null(stay);
        else Assert.Equal(Stay(start, end!), stay);
    }

    [Fact]
    public void Online_start_must_match_the_period_type()
    {
        Assert.Equal(Stay("2026-10-12", "2026-10-16"), StayPeriodFactory.ForOnlineStart(PeriodType.Midweek, D("2026-10-12")));
        Assert.Throws<DomainValidationException>(() => StayPeriodFactory.ForOnlineStart(PeriodType.Midweek, D("2026-10-13")));
        Assert.Throws<DomainValidationException>(() => StayPeriodFactory.ForOnlineStart(PeriodType.Weekend, D("2026-10-12")));
        Assert.Throws<DomainValidationException>(() => StayPeriodFactory.ForOnlineStart(PeriodType.LongWeekend, D("2026-10-16")));
        Assert.True(StayPeriodFactory.IsValidOnlineShape(PeriodType.Week, Stay("2026-10-16", "2026-10-23")));
        Assert.False(StayPeriodFactory.IsValidOnlineShape(PeriodType.Week, Stay("2026-10-16", "2026-10-22")));
    }
}

public class OverlapTests
{
    [Fact]
    public void Same_day_changeover_does_not_overlap() =>
        Assert.False(Stay("2026-10-09", "2026-10-12").Overlaps(Stay("2026-10-12", "2026-10-16")));

    [Fact]
    public void Bookings_sharing_a_night_overlap() =>
        Assert.True(Stay("2026-10-09", "2026-10-13").Overlaps(Stay("2026-10-12", "2026-10-16")));

    [Fact]
    public void Midweek_ending_on_a_long_weekend_first_night_does_not_overlap() =>
        Assert.False(Stay("2026-09-28", "2026-10-02").Overlaps(new NightRange(D("2026-10-02"), D("2026-10-04"))));

    [Fact]
    public void Midweek_including_christmas_first_night_overlaps() =>
        Assert.True(Stay("2026-12-17", "2026-12-21").Overlaps(new NightRange(D("2026-12-20"), D("2027-01-05"))));

    [Fact]
    public void Stay_starting_on_the_last_night_overlaps() =>
        Assert.True(Stay("2027-01-05", "2027-01-08").Overlaps(new NightRange(D("2026-12-20"), D("2027-01-05"))));

    [Fact]
    public void Stay_starting_after_the_last_night_does_not_overlap() =>
        Assert.False(Stay("2027-01-06", "2027-01-08").Overlaps(new NightRange(D("2026-12-20"), D("2027-01-05"))));

    [Fact]
    public void Stay_needs_start_before_end() =>
        Assert.Throws<DomainValidationException>(() => Stay("2026-10-12", "2026-10-12"));

    [Fact]
    public void Availability_ignores_standbys_cancelled_and_lapsed_holds()
    {
        var stay = Stay("2026-10-12", "2026-10-16");
        var cancelled = StaffBooking("2026-10-12", "2026-10-16");
        cancelled.Cancel();
        var bookings = new[]
        {
            StaffBooking("2026-10-12", "2026-10-16", standby: true),
            cancelled,
            Hold("2026-10-12", "2026-10-16", Now.AddMinutes(-1)),
        };
        Assert.False(BookingAvailabilityService.FindConflicts(stay, bookings, [], Now).Any);
    }

    [Fact]
    public void Availability_is_blocked_by_active_bookings_live_holds_and_unavailabilities()
    {
        var stay = Stay("2026-10-12", "2026-10-16");
        Assert.True(BookingAvailabilityService.FindConflicts(stay, [StaffBooking("2026-10-15", "2026-10-18")], [], Now).Any);
        Assert.True(BookingAvailabilityService.FindConflicts(stay, [Hold("2026-10-12", "2026-10-16", Now.AddMinutes(5))], [], Now).Any);
        var u = BoatUnavailability.Create(new BoatId("boat-1"), D("2026-10-15"), D("2026-10-15"), null, Now);
        Assert.True(BookingAvailabilityService.FindConflicts(stay, [], [u], Now).Any);
        var after = BoatUnavailability.Create(new BoatId("boat-1"), D("2026-10-16"), D("2026-10-20"), null, Now);
        Assert.False(BookingAvailabilityService.FindConflicts(stay, [], [after], Now).Any);
    }

    [Fact]
    public void EnsureAvailable_names_the_conflicting_reference()
    {
        var existing = StaffBooking("2026-10-12", "2026-10-16");
        var e = Assert.Throws<ConflictException>(() =>
            BookingAvailabilityService.EnsureAvailable(Stay("2026-10-14", "2026-10-18"), [existing], [], Now));
        Assert.Equal(BookingAvailabilityService.TakenMessage, e.Message);
        Assert.Contains(existing.Reference, e.ConflictingReferences);
    }

    [Fact]
    public void Excluded_booking_does_not_conflict_with_itself()
    {
        var existing = StaffBooking("2026-10-12", "2026-10-16");
        Assert.False(BookingAvailabilityService.FindConflicts(existing.Stay, [existing], [], Now, existing.Id).Any);
    }

    [Fact]
    public void Unavailability_overlapping_an_active_booking_is_rejected_with_its_reference()
    {
        var existing = StaffBooking("2026-10-12", "2026-10-16");
        var e = Assert.Throws<ConflictException>(() =>
            BookingAvailabilityService.EnsureUnavailabilityAllowed(new NightRange(D("2026-10-15"), D("2026-10-20")), [existing], Now));
        Assert.Contains(existing.Reference!, e.Message);
        BookingAvailabilityService.EnsureUnavailabilityAllowed(new NightRange(D("2026-10-16"), D("2026-10-20")), [existing], Now);
    }
}

public class TimeZoneTests
{
    [Fact]
    public void Zone_label_follows_daylight_saving()
    {
        Assert.Equal("AEST/NSW", SydneyTime.ZoneLabel(D("2026-07-01")));
        Assert.Equal("AEDT/NSW", SydneyTime.ZoneLabel(D("2027-01-15")));
    }

    [Fact]
    public void Check_in_instant_is_11am_sydney()
    {
        Assert.Equal(new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero), SydneyTime.CheckInInstant(D("2027-01-15")));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero), SydneyTime.CheckInInstant(D("2026-07-01")));
    }

    [Fact]
    public void Times_and_dates_use_australian_formats()
    {
        Assert.Equal("11am", Dates.FormatTime(new TimeOnly(11, 0)));
        Assert.Equal("11:30am", Dates.FormatTime(new TimeOnly(11, 30)));
        Assert.Equal("12pm", Dates.FormatTime(new TimeOnly(12, 0)));
        Assert.Equal("15/01/2027", Dates.Format(D("2027-01-15")));
        Assert.Equal("$1,234.50", Money.Format(1234.5m));
    }
}
