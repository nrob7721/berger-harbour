using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreAddonDefinitionRepository : IAddonDefinitionRepository
{
    private readonly FirestoreDb db;
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreAddonDefinitionRepository(FirestoreDb db, VersionedDocuments documents)
    {
        this.db = db;
        collection = db.Collection(FirestoreCollections.Addons);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<AddonDefinitionDocument>().ToDomain(d.Id)).OrderBy(a => a.SortOrder).ToList();

    public async Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<AddonDefinitionDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task SaveAsync(AddonDefinition addon, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(addon.Id.Value), addon, AddonDefinitionDocument.From(addon), ct);

    public async Task SaveAllAsync(IReadOnlyList<AddonDefinition> addons, CancellationToken ct = default)
    {
        await db.RunTransactionAsync(
            async tx =>
            {
                foreach (var addon in addons)
                {
                    VersionedDocuments.EnsureVersion(await tx.GetSnapshotAsync(collection.Document(addon.Id.Value), ct), addon.Version);
                }

                foreach (var addon in addons)
                {
                    var document = AddonDefinitionDocument.From(addon);
                    document.Version = addon.Version + 1;
                    tx.Set(collection.Document(addon.Id.Value), document);
                }
            },
            cancellationToken: ct);
        foreach (var addon in addons)
        {
            addon.SetPersistedVersion(addon.Version + 1);
        }
    }
}
