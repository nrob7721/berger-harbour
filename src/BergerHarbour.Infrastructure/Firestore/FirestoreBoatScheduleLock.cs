using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// The per-boat exclusive section (§7.2): a Firestore transaction that first reads and then updates
/// <c>boatLocks/{boatId}</c>, so concurrent transactions on the same boat are serialised. Contended transactions are
/// retried by Firestore and re-run the work against fresh reads.
/// </summary>
public sealed class FirestoreBoatScheduleLock : IBoatScheduleLock
{
    private static readonly TransactionOptions Options = TransactionOptions.ForMaxAttempts(15);
    private readonly FirestoreDb db;

    public FirestoreBoatScheduleLock(FirestoreDb db)
    {
        this.db = db;
    }

    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work)
    {
        FirestoreBoatScheduleSession? committed = null;
        var lockReference = db.Collection(FirestoreCollections.BoatLocks).Document(boatId.Value);
        var result = await db.RunTransactionAsync(
            async tx =>
            {
                var lockSnapshot = await tx.GetSnapshotAsync(lockReference);
                var counter = lockSnapshot.Exists ? lockSnapshot.GetValue<long>("counter") : 0;
                var session = new FirestoreBoatScheduleSession(db, tx, boatId);
                var value = await work(session);
                await session.FlushAsync();
                tx.Set(lockReference, new BoatLockDocument { Counter = counter + 1, UpdatedAt = Timestamp.GetCurrentTimestamp() });
                committed = session;
                return value;
            },
            Options);
        committed!.ApplyPersistedVersions();
        return result;
    }
}
