using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreBookingDraftRepository(FirestoreDb db) : IBookingDraftRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.BookingDrafts);

    public async Task<BookingDraft?> GetAsync(BookingDraftId id, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id.Value) || id.Value.Contains('/'))
        {
            return null;
        }

        var snapshot = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return snapshot.Exists ? ToBookingDraft(snapshot.Id, snapshot.ConvertTo<BookingDraftDocument>()) : null;
    }

    public Task<IReadOnlyList<BookingDraft>> ListByEmailAsync(EmailAddress email, CancellationToken ct = default) =>
        ListAsync(Collection.WhereEqualTo("email", email.Value).OrderByDescending("lastActivityAt"), ct);

    public Task<IReadOnlyList<BookingDraft>> ListInactiveSinceAsync(DateTimeOffset lastActivityBefore, CancellationToken ct = default) =>
        ListAsync(Collection.WhereLessThan("lastActivityAt", Ts(lastActivityBefore)), ct);

    public Task<IReadOnlyList<BookingDraft>> ListCreatedBeforeAsync(DateTimeOffset createdBefore, CancellationToken ct = default) =>
        ListAsync(Collection.WhereLessThan("createdDate", Ts(createdBefore)), ct);

    public async Task SaveAsync(BookingDraft draft, CancellationToken ct = default)
    {
        var s = draft.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        draft.MarkPersisted();
    }

    public Task DeleteAsync(BookingDraft draft, CancellationToken ct = default) =>
        VersionedWriter.DeleteAsync(db, Collection.Document(draft.Id.Value), draft.Version, ct);

    private static async Task<IReadOnlyList<BookingDraft>> ListAsync(Query query, CancellationToken ct)
    {
        var snapshot = await query.GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToBookingDraft(d.Id, d.ConvertTo<BookingDraftDocument>())).ToList();
    }
}
