using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreSeasonRepository : ISeasonRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreSeasonRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.Seasons);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<SeasonDocument>().ToDomain(d.Id)).ToList();

    public async Task<Season?> GetAsync(SeasonId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<SeasonDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task SaveAsync(Season season, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(season.Id.Value), season, SeasonDocument.From(season), ct);

    public async Task DeleteAsync(Season season, CancellationToken ct = default) =>
        await collection.Document(season.Id.Value).DeleteAsync(cancellationToken: ct);
}
