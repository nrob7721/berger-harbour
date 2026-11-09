using BergerHarbour.Application.Abstractions;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>A counter document per reference prefix (<c>counters/ref-SMITH-270115</c>), incremented transactionally.</summary>
public sealed class FirestoreBookingReferenceGenerator : IBookingReferenceGenerator
{
    private readonly FirestoreDb db;

    public FirestoreBookingReferenceGenerator(FirestoreDb db)
    {
        this.db = db;
    }

    public Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default)
    {
        var reference = db.Collection(FirestoreCollections.Counters).Document("ref-" + prefix);
        return db.RunTransactionAsync(
            async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(reference, ct);
                var next = snapshot.Exists ? snapshot.GetValue<int>("next") : 0;
                tx.Set(reference, new ReferenceCounterDocument { Next = next + 1 });
                return next;
            },
            TransactionOptions.ForMaxAttempts(15),
            ct);
    }
}
