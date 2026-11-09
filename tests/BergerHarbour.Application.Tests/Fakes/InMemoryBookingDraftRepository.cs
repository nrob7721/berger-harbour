using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBookingDraftRepository(InMemoryDatabase db) : IBookingDraftRepository
{
    public Task<BookingDraft?> GetAsync(BookingDraftId id, CancellationToken ct = default) => Task.FromResult(db.Drafts.Get(id));

    public Task SaveAsync(BookingDraft draft, CancellationToken ct = default)
    {
        db.Drafts.Save(draft);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BookingDraft>> ListAbandonCandidatesAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        Where(d => d.LastActivityAt < cutoff && !d.IsConverted && d.AbandonedNotifiedAt is null);

    public Task<IReadOnlyList<BookingDraft>> ListByEmailAsync(EmailAddress email, CancellationToken ct = default) => Where(d => d.Contents.Email == email);

    public Task<IReadOnlyList<BookingDraft>> ListCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Where(d => d.CreatedDate < cutoff);

    public Task DeleteAsync(BookingDraftId id, CancellationToken ct = default)
    {
        db.Drafts.Delete(id);
        return Task.CompletedTask;
    }

    private Task<IReadOnlyList<BookingDraft>> Where(Func<BookingDraft, bool> predicate) =>
        Task.FromResult<IReadOnlyList<BookingDraft>>(db.Drafts.All.Where(predicate).ToList());
}
