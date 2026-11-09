using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// The repositories' shared helper for versioned writes: inside a transaction, the stored version must equal the
/// aggregate's (0 = must not exist yet), and the write stores version + 1.
/// </summary>
internal sealed class VersionedDocuments
{
    private readonly FirestoreDb db;

    public VersionedDocuments(FirestoreDb db)
    {
        this.db = db;
    }

    /// <summary>Ids from requests are only used as document ids when Firestore accepts them.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 1500 && !id.Contains('/') && id != "." && id != ".." && !(id.StartsWith("__") && id.EndsWith("__"));

    public static void EnsureVersion(DocumentSnapshot snapshot, int expectedVersion)
    {
        var stored = snapshot.Exists ? snapshot.GetValue<int>("version") : 0;
        if (snapshot.Exists ? stored != expectedVersion || expectedVersion == 0 : expectedVersion != 0)
        {
            throw new ConcurrencyException();
        }
    }

    public async Task SaveAsync<TDocument>(DocumentReference reference, AggregateRoot aggregate, TDocument document, CancellationToken ct)
        where TDocument : IVersionedDocument
    {
        var expected = aggregate.Version;
        document.Version = expected + 1;
        await db.RunTransactionAsync(
            async tx =>
            {
                EnsureVersion(await tx.GetSnapshotAsync(reference, ct), expected);
                tx.Set(reference, document);
            },
            cancellationToken: ct);
        aggregate.SetPersistedVersion(expected + 1);
    }

    public async Task<TDocument?> GetAsync<TDocument>(DocumentReference reference, CancellationToken ct)
        where TDocument : class
    {
        var snapshot = await reference.GetSnapshotAsync(ct);
        return snapshot.Exists ? snapshot.ConvertTo<TDocument>() : null;
    }

    public static async Task<IReadOnlyList<TDocument>> QueryAsync<TDocument>(Query query, CancellationToken ct)
        where TDocument : class =>
        (await query.GetSnapshotAsync(ct)).Documents.Select(d => d.ConvertTo<TDocument>()).ToList();
}
