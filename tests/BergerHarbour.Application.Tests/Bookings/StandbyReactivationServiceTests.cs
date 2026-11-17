using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

/// <summary>A superseded stand-by is reactivated by moving it (decision 40).</summary>
public class StandbyReactivationServiceTests
{
    private readonly ApplicationHarness h = new();

    private int SupersededEmails => h.CountEmails("Stand-by booking superseded");

    private async Task<string> SupersededStandbyAsync()
    {
        var standby = await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", standby: true, email: "credit@example.com"));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        Assert.Equal(BookingStatus.Superseded, h.Booking(standby.Booking.Id).Status);
        return standby.Booking.Id;
    }

    [Fact]
    public async Task Moving_it_to_free_dates_makes_it_an_Active_standby_and_superseding_it_again_sends_8_again()
    {
        var id = await SupersededStandbyAsync();
        Assert.Equal(1, SupersededEmails);

        var moved = (await h.Staff.UpdateAsync(id, UpdateRequest(await h.Staff.GetAsync(id), r => r with
        {
            StartDate = D("2027-03-15"), EndDate = D("2027-03-19"),
        }))).Booking;
        Assert.Equal((BookingStatus.Active, true), (moved.Status, moved.IsStandby));

        await h.Staff.CreateAsync(h.StaffRequest("2027-03-15", "2027-03-19", email: "third@example.com", lastName: "Brown"));
        Assert.Equal(BookingStatus.Superseded, h.Booking(id).Status);
        Assert.Equal(2, SupersededEmails);
    }

    [Fact]
    public async Task Moving_it_to_another_boat_reactivates_it()
    {
        var id = await SupersededStandbyAsync();
        h.Activate("misty-blue");
        var moved = (await h.Staff.UpdateAsync(id, UpdateRequest(await h.Staff.GetAsync(id), r => r with { BoatId = h.Boat("misty-blue").Id.Value }))).Booking;
        Assert.Equal(BookingStatus.Active, moved.Status);
    }

    [Fact]
    public async Task Moving_it_onto_taken_dates_is_409_and_saves_nothing()
    {
        var id = await SupersededStandbyAsync();
        var taken = await h.Staff.CreateAsync(h.StaffRequest("2027-03-15", "2027-03-19", email: "third@example.com", lastName: "Brown"));
        var e = await Assert.ThrowsAsync<ConflictException>(() => h.Staff.UpdateAsync(id, UpdateRequest(h.Staff.GetAsync(id).Result, r => r with
        {
            StartDate = D("2027-03-16"), EndDate = D("2027-03-18"),
        })));
        Assert.Equal([taken.Booking.Reference!], e.ConflictingReferences);
        var stored = h.Booking(id);
        Assert.Equal((BookingStatus.Superseded, D("2027-03-08")), (stored.Status, stored.StartDate));
    }

    [Fact]
    public async Task A_save_that_changes_neither_boat_nor_dates_keeps_it_Superseded_and_its_status_and_flag_are_locked()
    {
        var id = await SupersededStandbyAsync();
        var kept = (await h.Staff.UpdateAsync(id, UpdateRequest(await h.Staff.GetAsync(id), r => r with { BookingComments = "Called" }))).Booking;
        Assert.Equal(BookingStatus.Superseded, kept.Status);

        var flag = await Assert.ThrowsAsync<DomainValidationException>(() => h.Staff.UpdateAsync(id, UpdateRequest(kept, r => r with
        {
            IsStandby = false, StartDate = D("2027-03-15"), EndDate = D("2027-03-19"),
        })));
        Assert.True(flag.Errors.Has("isStandby"));
        var status = await Assert.ThrowsAsync<DomainValidationException>(() => h.Staff.UpdateAsync(id, UpdateRequest(kept, r => r with
        {
            Status = BookingStatus.Active,
        })));
        Assert.True(status.Errors.Has("status"));
    }
}
