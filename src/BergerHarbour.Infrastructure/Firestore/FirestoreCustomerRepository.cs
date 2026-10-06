using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using Grpc.Core;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// Email uniqueness is enforced with a customerEmails/{normalisedEmail} index document written in the same
/// transaction as the customer.
/// </summary>
public sealed class FirestoreCustomerRepository(FirestoreDb db) : ICustomerRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.Customers);

    private CollectionReference Emails => db.Collection(FirestoreCollections.CustomerEmails);

    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct = default)
    {
        var snapshot = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return snapshot.Exists ? ToCustomer(snapshot.Id, snapshot.ConvertTo<CustomerDocument>()) : null;
    }

    public async Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken ct = default)
    {
        var index = await Emails.Document(email.Value).GetSnapshotAsync(ct);
        return index.Exists ? await GetAsync(new CustomerId(index.ConvertTo<CustomerEmailDocument>().CustomerId), ct) : null;
    }

    public async Task<IReadOnlyList<Customer>> GetManyAsync(IEnumerable<CustomerId> ids, CancellationToken ct = default)
    {
        var references = ids.Distinct().Select(i => Collection.Document(i.Value)).ToList();
        if (references.Count == 0)
        {
            return [];
        }

        var snapshots = await db.GetAllSnapshotsAsync(references, ct);
        return snapshots.Where(s => s.Exists).Select(s => ToCustomer(s.Id, s.ConvertTo<CustomerDocument>())).ToList();
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
    {
        var snapshot = await Collection.GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToCustomer(d.Id, d.ConvertTo<CustomerDocument>())).ToList();
    }

    public async Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        var s = customer.ToSnapshot();
        if (s.Version == 0)
        {
            // A new customer and its email index are created atomically. Both writes require the document not to
            // exist, so a concurrent insert of the same email fails without any read lock to contend on.
            var batch = db.StartBatch();
            batch.Create(Collection.Document(s.Id), ToDocument(s, 1));
            batch.Create(Emails.Document(s.Email), new CustomerEmailDocument { CustomerId = s.Id });
            try
            {
                await batch.CommitAsync(ct);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                throw new DuplicateCustomerEmailException(s.Email);
            }
        }
        else
        {
            await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        }

        customer.MarkPersisted();
    }
}
