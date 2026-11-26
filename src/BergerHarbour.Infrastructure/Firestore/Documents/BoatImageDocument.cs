using BergerHarbour.Domain.Boats;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BoatImageDocument
{
    [FirestoreProperty("url")]
    public string Url { get; set; } = string.Empty;

    [FirestoreProperty("description")]
    public string? Description { get; set; }

    public static BoatImageDocument From(BoatImage i) => new() { Url = i.Url, Description = i.Description };

    public BoatImage ToDomain() => BoatImage.Create(Url, Description);
}
