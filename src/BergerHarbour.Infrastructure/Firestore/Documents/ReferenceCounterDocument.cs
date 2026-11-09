using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

/// <summary><c>counters/ref-{LASTNAME-YYMMDD}</c>: how many references already have the prefix.</summary>
[FirestoreData]
public sealed class ReferenceCounterDocument
{
    [FirestoreProperty("next")]
    public int Next { get; set; }
}
