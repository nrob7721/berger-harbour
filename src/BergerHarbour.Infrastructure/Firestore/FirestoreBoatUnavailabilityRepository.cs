using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreBoatUnavailabilityRepository(FirestoreDb db) : IBoatUnavailabilityRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.BoatUnavailabilities);

    public async Task<BoatUnavailability?> GetAsync(UnavailabilityId id, CancellationToken ct = default)
    {
        var snapshot = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return snapshot.Exists ? ToUnavailability(snapshot.Id, snapshot.ConvertTo<UnavailabilityDocument>()) : null;
    }

    public async Task<IReadOnlyList<BoatUnavailability>> ListByBoatAsync(BoatId boatId, CancellationToken ct = default)
    {
        var snapshot = await Collection.WhereEqualTo("boatId", boatId.Value).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToUnavailability(d.Id, d.ConvertTo<UnavailabilityDocument>())).ToList();
    }

    /// <summary>Queries firstNight &lt; to and filters lastNight &gt;= from in memory.</summary>
    public async Task<IReadOnlyList<BoatUnavailability>> ListOverlappingAsync(DateOnly from, DateOnly to,
        BoatId? boatId = null, CancellationToken ct = default)
    {
        Query query = Collection;
        if (boatId is { } boat)
        {
            query = query.WhereEqualTo("boatId", boat.Value);
        }

        var fromKey = Date(from);
        var snapshot = await query.WhereLessThan("firstNight", Date(to)).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => (d.Id, Doc: d.ConvertTo<UnavailabilityDocument>()))
            .Where(x => string.CompareOrdinal(x.Doc.LastNight, fromKey) >= 0)
            .Select(x => ToUnavailability(x.Id, x.Doc))
            .ToList();
    }

    public Task DeleteAsync(BoatUnavailability unavailability, CancellationToken ct = default) =>
        VersionedWriter.DeleteAsync(db, Collection.Document(unavailability.Id.Value), unavailability.Version, ct);
}
