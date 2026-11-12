using BergerHarbour.Domain.Customers;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class AddressDocument
{
    [FirestoreProperty("line1")]
    public string? Line1 { get; set; }

    [FirestoreProperty("line2")]
    public string? Line2 { get; set; }

    [FirestoreProperty("locality")]
    public string? Locality { get; set; }

    [FirestoreProperty("state")]
    public string? State { get; set; }

    [FirestoreProperty("postcode")]
    public string? Postcode { get; set; }

    [FirestoreProperty("country")]
    public string? Country { get; set; }

    public static AddressDocument? From(Address? a) => a is null
        ? null
        : new() { Line1 = a.Line1, Line2 = a.Line2, Locality = a.Locality, State = a.State, Postcode = a.Postcode, Country = a.Country };

    public Address? ToDomain() => Address.Rehydrate(Line1, Line2, Locality, State, Postcode, Country);
}
