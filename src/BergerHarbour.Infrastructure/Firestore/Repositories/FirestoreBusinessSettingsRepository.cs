using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreBusinessSettingsRepository : IBusinessSettingsRepository
{
    private readonly DocumentReference reference;
    private readonly VersionedDocuments documents;

    internal FirestoreBusinessSettingsRepository(FirestoreDb db, VersionedDocuments documents)
    {
        reference = db.Collection(FirestoreCollections.Settings).Document(FirestoreCollections.BusinessSettingsId);
        this.documents = documents;
    }

    public async Task<BusinessSettings?> GetAsync(CancellationToken ct = default) =>
        (await documents.GetAsync<BusinessSettingsDocument>(reference, ct))?.ToDomain();

    public Task SaveAsync(BusinessSettings settings, CancellationToken ct = default) =>
        documents.SaveAsync(reference, settings, BusinessSettingsDocument.From(settings), ct);
}
