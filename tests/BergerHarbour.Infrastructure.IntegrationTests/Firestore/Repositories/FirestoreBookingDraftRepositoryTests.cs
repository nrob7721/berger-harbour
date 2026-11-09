using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBookingDraftRepositoryTests(EmulatorFixture fx)
{
    [Fact]
    public async Task A_draft_round_trips_and_is_found_by_email()
    {
        var email = $"draft-{Guid.NewGuid():N}@example.com";
        var created = await fx.Get<BookingDraftService>().CreateAsync(new CreateDraftRequest("pacific-blue", PeriodType.Midweek,
            EmulatorFixture.D("2027-03-08"), 2, 1, "Jane", "Smith", email, "+61412345678", "t"), null);
        var draft = (await fx.Get<IBookingDraftRepository>().GetAsync(new BookingDraftId(created.DraftId)))!;
        Assert.Equal((2, 1), (draft.Contents.NumberOfAdults, draft.Contents.NumberOfChildren));
        Assert.Single(await fx.Get<IBookingDraftRepository>().ListByEmailAsync(EmailAddress.Create(email)));
        await fx.Get<IBookingDraftRepository>().DeleteAsync(draft.Id);
        Assert.Null(await fx.Get<IBookingDraftRepository>().GetAsync(draft.Id));
    }
}
