using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class AddonRequestDocument
{
    [FirestoreProperty("addonId")]
    public string AddonId { get; set; } = string.Empty;

    [FirestoreProperty("quantity")]
    public int Quantity { get; set; }

    public static AddonRequestDocument From(AddonRequest r) => new() { AddonId = r.AddonId.Value, Quantity = r.Quantity };

    public AddonRequest ToDomain() => new(new AddonId(AddonId), Quantity);
}
