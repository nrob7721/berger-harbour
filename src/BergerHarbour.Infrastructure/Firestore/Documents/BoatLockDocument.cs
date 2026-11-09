using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

/// <summary><c>boatLocks/{boatId}</c>: read then updated by every exclusive section on the boat, which serialises them.</summary>
[FirestoreData]
public sealed class BoatLockDocument
{
    [FirestoreProperty("counter")]
    public long Counter { get; set; }

    [FirestoreProperty("updatedAt")]
    public Timestamp UpdatedAt { get; set; }
}
