using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class BookingDraftTests
{
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(2);
    private static readonly BoatId Boat = BoatId.New();
    private static readonly Stay Stay = new(D(2026, 12, 7), D(2026, 12, 11));

    private static BookingDraft Draft(DateTimeOffset createdAt, string email = "jane@example.com") =>
        BookingDraft.Create(Boat, PeriodType.Midweek, Stay, "Jane", email, "0412 345 678", createdAt);

    [Fact]
    public void Ids_are_long_random_and_url_safe()
    {
        var id = BookingDraftId.New().Value;
        Assert.True(id.Length >= 32);
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('+', id);
        Assert.NotEqual(id, BookingDraftId.New().Value);
    }

    [Fact]
    public void Updates_record_progress_and_activity()
    {
        var draft = Draft(Now);
        Assert.Equal(BookingStep.Details, draft.LastStepReached);
        Assert.Equal("+61412345678", draft.MobileNumber);

        draft.Update(PeriodType.Midweek, Stay, "Jane", "jane@example.com", "0412 345 678", 4, 2,
            [new DraftAddonRequest(AddonId.New(), 2)], BookingStep.Addons, Now.AddMinutes(5));
        Assert.Equal(BookingStep.Addons, draft.LastStepReached);
        Assert.Equal(Now.AddMinutes(5), draft.LastActivityAt);
        Assert.Equal(4, draft.NumberOfAdults);

        // Going back a step keeps the furthest step reached.
        draft.Update(PeriodType.Midweek, Stay, "Jane", "jane@example.com", "0412 345 678", 4, 2, [], BookingStep.Guests,
            Now.AddMinutes(6));
        Assert.Equal(BookingStep.Addons, draft.LastStepReached);
    }

    [Fact]
    public void A_converted_draft_cannot_be_updated_and_is_never_abandoned()
    {
        var draft = Draft(Now);
        var bookingId = BookingId.New();
        draft.MarkConverted(bookingId, Now);
        draft.MarkConverted(bookingId, Now); // idempotent
        Assert.Throws<InvalidStateTransitionException>(() => draft.MarkConverted(BookingId.New(), Now));
        Assert.Throws<InvalidStateTransitionException>(() => draft.Update(PeriodType.Midweek, Stay, "Jane",
            "jane@example.com", "0412 345 678", 1, 0, [], BookingStep.Guests, Now));
        Assert.False(draft.IsAbandoned(Now.AddDays(1), AbandonedAfter));
    }

    [Fact]
    public void Abandoned_after_the_inactivity_threshold()
    {
        var draft = Draft(Now);
        Assert.False(draft.IsAbandoned(Now.AddHours(2), AbandonedAfter));
        Assert.True(draft.IsAbandoned(Now.AddHours(2).AddSeconds(1), AbandonedAfter));
    }

    [Fact]
    public void Deleted_after_the_retention_period()
    {
        var draft = Draft(Now);
        Assert.False(draft.IsPastRetention(Now.AddDays(30), TimeSpan.FromDays(30)));
        Assert.True(draft.IsPastRetention(Now.AddDays(30).AddMinutes(1), TimeSpan.FromDays(30)));
    }

    [Fact]
    public void Staff_are_alerted_once_for_an_abandoned_draft()
    {
        var draft = Draft(Now);
        var later = Now.AddHours(3);
        Assert.True(AbandonedDraftPolicy.ShouldNotify(draft, [draft], [], later, AbandonedAfter));
        draft.MarkAbandonedNotified(later);
        Assert.False(AbandonedDraftPolicy.ShouldNotify(draft, [draft], [], later.AddHours(1), AbandonedAfter));
    }

    [Fact]
    public void Only_the_latest_draft_per_email_is_reported()
    {
        var older = Draft(Now);
        var newer = Draft(Now.AddMinutes(30), "JANE@example.com");
        var later = Now.AddHours(5);
        Assert.False(AbandonedDraftPolicy.ShouldNotify(older, [older, newer], [], later, AbandonedAfter));
        Assert.True(AbandonedDraftPolicy.ShouldNotify(newer, [older, newer], [], later, AbandonedAfter));
    }

    [Fact]
    public void Not_reported_once_a_booking_for_that_email_became_active_after_the_draft()
    {
        var draft = Draft(Now);
        var later = Now.AddHours(5);
        var bookedAfter = StaffBooking(Boat, D(2027, 1, 4), D(2027, 1, 8), createdAt: Now.AddHours(1));
        var bookedBefore = StaffBooking(Boat, D(2027, 1, 4), D(2027, 1, 8), createdAt: Now.AddDays(-30));
        var expiredHold = OnlineHold(Boat, D(2027, 1, 4), D(2027, 1, 8), Now.AddMinutes(40));
        expiredHold.Expire(Now.AddHours(1));

        Assert.False(AbandonedDraftPolicy.ShouldNotify(draft, [draft], [bookedAfter], later, AbandonedAfter));
        Assert.True(AbandonedDraftPolicy.ShouldNotify(draft, [draft], [bookedBefore, expiredHold], later, AbandonedAfter));
    }
}
