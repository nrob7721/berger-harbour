using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

/// <summary>Unavailability reads; writes go through <see cref="FirestoreBoatScheduleLock"/>.</summary>
public sealed class FirestoreBoatUnavailabilityRepository : IBoatUnavailabilityRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreBoatUnavailabilityRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.BoatUnavailabilities);
        this.documents = documents;
    }

    public async Task<BoatUnavailability?> GetAsync(BoatUnavailabilityId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<BoatUnavailabilityDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default) =>
        ListAsync(collection.WhereEqualTo("boatId", boatId.Value), ct);

    public async Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        (await ListAsync(collection.WhereGreaterThanOrEqualTo("lastNight", FirestoreValues.Date(from)), ct))
            .Where(u => u.Nights.FirstNight < to)
            .ToList();

    private static async Task<IReadOnlyList<BoatUnavailability>> ListAsync(Query query, CancellationToken ct) =>
        (await query.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<BoatUnavailabilityDocument>().ToDomain(d.Id)).ToList();
}
