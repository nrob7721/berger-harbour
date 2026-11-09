using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Tests.Support;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.BookingDrafts;

public class BookingDraftTests
{
    private static readonly DateTimeOffset Created = At("2026-11-09T00:00:00Z");
    private static readonly TimeSpan After = TimeSpan.FromHours(2);

    private static BookingDraft Draft() => BookingDraft.Create(BookingDraftId.New(), new DraftContents(TestBookings.PacificBlue, PeriodType.Midweek,
        StayRange.Create(D("2027-03-08"), D("2027-03-12")), 2, 0, "Jane", "Smith", EmailAddress.Create("jane@example.com"),
        MobileNumber.Create("0412345678"), [], DraftStep.Details), Created);

    [Fact]
    public void A_draft_is_abandoned_after_the_inactivity_threshold()
    {
        var draft = Draft();
        Assert.False(draft.IsAbandoned(Created + After, After));
        Assert.True(draft.IsAbandoned(Created + After + TimeSpan.FromSeconds(1), After));
    }

    [Fact]
    public void Activity_resets_the_threshold()
    {
        var draft = Draft();
        draft.Update(draft.Contents with { LastStepReached = DraftStep.Summary }, Created.AddHours(1));
        Assert.False(draft.IsAbandoned(Created.AddHours(2.5), After));
    }

    [Fact]
    public void A_converted_draft_is_never_abandoned_and_cannot_be_updated()
    {
        var draft = Draft();
        draft.MarkConverted(BookingId.New(), Created.AddMinutes(10));
        Assert.False(draft.IsAbandoned(Created.AddDays(1), After));
        Assert.Throws<NotFoundException>(() => draft.Update(draft.Contents, Created.AddMinutes(11)));
    }

    [Fact]
    public void Retention_is_from_the_created_date() => Assert.True(Draft().IsPastRetention(Created.AddDays(30).AddSeconds(1), TimeSpan.FromDays(30)));

    [Fact]
    public void Ids_are_random_and_at_least_128_bits() => Assert.True(BookingDraftId.New().Value.Length >= 22);
}
