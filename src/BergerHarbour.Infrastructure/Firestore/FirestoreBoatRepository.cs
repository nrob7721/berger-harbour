using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreBoatRepository(FirestoreDb db) : IBoatRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.Boats);

    public async Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id.Value) || id.Value.Contains('/'))
        {
            return null;
        }

        var snapshot = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return snapshot.Exists ? ToBoat(snapshot.Id, snapshot.ConvertTo<BoatDocument>()) : null;
    }

    public async Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var snapshot = await Collection.WhereEqualTo("slug", slug).Limit(1).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToBoat(d.Id, d.ConvertTo<BoatDocument>())).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default)
    {
        var snapshot = await Collection.GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToBoat(d.Id, d.ConvertTo<BoatDocument>())).ToList();
    }

    public async Task SaveAsync(Boat boat, CancellationToken ct = default)
    {
        var s = boat.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        boat.MarkPersisted();
    }
}
