using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

/// <summary><c>customerEmails/{normalisedEmail}</c>: the uniqueness index, written in the customer's transaction.</summary>
[FirestoreData]
public sealed class CustomerEmailDocument
{
    [FirestoreProperty("customerId")]
    public string CustomerId { get; set; } = string.Empty;
}
