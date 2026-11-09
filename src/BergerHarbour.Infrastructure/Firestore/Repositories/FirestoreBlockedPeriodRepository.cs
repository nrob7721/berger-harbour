using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreBlockedPeriodRepository : IBlockedPeriodRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreBlockedPeriodRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.BlockedPeriods);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<BlockedPeriodDocument>().ToDomain(d.Id)).ToList();

    public async Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<BlockedPeriodDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task SaveAsync(BlockedPeriod period, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(period.Id.Value), period, BlockedPeriodDocument.From(period), ct);

    public async Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default) =>
        await collection.Document(period.Id.Value).DeleteAsync(cancellationToken: ct);
}
