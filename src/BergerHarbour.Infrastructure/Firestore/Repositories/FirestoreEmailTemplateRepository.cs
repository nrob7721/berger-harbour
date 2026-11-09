using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreEmailTemplateRepository : IEmailTemplateRepository
{
    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreEmailTemplateRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.EmailTemplates);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents
            .Where(d => Enum.IsDefined(typeof(EmailTemplateKey), d.Id))
            .Select(d => d.ConvertTo<EmailTemplateDocument>().ToDomain(d.Id))
            .ToList();

    public async Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default) =>
        (await documents.GetAsync<EmailTemplateDocument>(collection.Document(key.ToString()), ct))?.ToDomain(key.ToString());

    public Task SaveAsync(EmailTemplate template, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(template.Key.ToString()), template, EmailTemplateDocument.From(template), ct);
}
