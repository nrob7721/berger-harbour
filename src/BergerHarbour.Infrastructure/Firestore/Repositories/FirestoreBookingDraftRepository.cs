using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreBookingDraftRepository : IBookingDraftRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreBookingDraftRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.BookingDrafts);
        this.documents = documents;
    }

    public async Task<BookingDraft?> GetAsync(BookingDraftId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<BookingDraftDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task SaveAsync(BookingDraft draft, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(draft.Id.Value), draft, BookingDraftDocument.From(draft), ct);

    public async Task<IReadOnlyList<BookingDraft>> ListAbandonCandidatesAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        (await ListAsync(collection.WhereLessThan("lastActivityAt", FirestoreValues.Instant(cutoff)), ct))
            .Where(d => !d.IsConverted && d.AbandonedNotifiedAt is null)
            .ToList();

    public Task<IReadOnlyList<BookingDraft>> ListByEmailAsync(EmailAddress email, CancellationToken ct = default) =>
        ListAsync(collection.WhereEqualTo("email", email.Value), ct);

    public Task<IReadOnlyList<BookingDraft>> ListCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        ListAsync(collection.WhereLessThan("createdDate", FirestoreValues.Instant(cutoff)), ct);

    public async Task DeleteAsync(BookingDraftId id, CancellationToken ct = default) => await collection.Document(id.Value).DeleteAsync(cancellationToken: ct);

    private static async Task<IReadOnlyList<BookingDraft>> ListAsync(Query query, CancellationToken ct) =>
        (await query.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<BookingDraftDocument>().ToDomain(d.Id)).ToList();
}
