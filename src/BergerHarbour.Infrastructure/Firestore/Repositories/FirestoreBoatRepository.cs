using BergerHarbour.Domain.Boats;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreBoatRepository : IBoatRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreBoatRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.Boats);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<BoatDocument>().ToDomain(d.Id)).ToList();

    public async Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<BoatDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public async Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var snapshot = await collection.WhereEqualTo("slug", slug.Trim().ToLowerInvariant()).Limit(1).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => d.ConvertTo<BoatDocument>().ToDomain(d.Id)).FirstOrDefault();
    }

    public Task SaveAsync(Boat boat, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(boat.Id.Value), boat, BoatDocument.From(boat), ct);
}
