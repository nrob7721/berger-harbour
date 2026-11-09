using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

/// <summary>Customers, with email uniqueness enforced by <c>customerEmails/{normalisedEmail}</c> in the same transaction.</summary>
public sealed class FirestoreCustomerRepository : ICustomerRepository
{
    private readonly FirestoreDb db;
    private readonly CollectionReference collection;
    private readonly CollectionReference emails;
    private readonly VersionedDocuments documents;

    internal FirestoreCustomerRepository(FirestoreDb db, VersionedDocuments documents)
    {
        this.db = db;
        collection = db.Collection(FirestoreCollections.Customers);
        emails = db.Collection(FirestoreCollections.CustomerEmails);
        this.documents = documents;
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default) =>
        (await collection.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<CustomerDocument>().ToDomain(d.Id)).ToList();

    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<CustomerDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public async Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default)
    {
        var index = await documents.GetAsync<CustomerEmailDocument>(EmailDocument(email.Value), ct);
        return index is null ? null : await GetAsync(new CustomerId(index.CustomerId), ct);
    }

    public async Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        var reference = collection.Document(customer.Id.Value);
        var expected = customer.Version;
        var document = CustomerDocument.From(customer);
        document.Version = expected + 1;
        await db.RunTransactionAsync(
            async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(reference, ct);
                VersionedDocuments.EnsureVersion(snapshot, expected);
                var previousEmail = snapshot.Exists ? snapshot.GetValue<string>("email") : null;
                var newIndex = EmailDocument(customer.Email.Value);
                if (previousEmail != customer.Email.Value)
                {
                    var taken = await tx.GetSnapshotAsync(newIndex, ct);
                    if (taken.Exists && taken.GetValue<string>("customerId") != customer.Id.Value)
                    {
                        throw new DuplicateCustomerEmailException(customer.Email.Value);
                    }

                    if (previousEmail is not null)
                    {
                        tx.Delete(EmailDocument(previousEmail));
                    }

                    tx.Set(newIndex, new CustomerEmailDocument { CustomerId = customer.Id.Value });
                }

                tx.Set(reference, document);
            },
            cancellationToken: ct);
        customer.SetPersistedVersion(expected + 1);
    }

    private DocumentReference EmailDocument(string normalisedEmail) => emails.Document(Uri.EscapeDataString(normalisedEmail));
}
