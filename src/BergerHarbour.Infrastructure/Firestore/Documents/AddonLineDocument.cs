using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class AddonLineDocument
{
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("addonId")]
    public string AddonId { get; set; } = string.Empty;

    [FirestoreProperty("nameSnapshot")]
    public string NameSnapshot { get; set; } = string.Empty;

    [FirestoreProperty("quantity")]
    public int Quantity { get; set; }

    [FirestoreProperty("unitPriceCents")]
    public long? UnitPriceCents { get; set; }

    public static AddonLineDocument From(AddonLine l) => new()
    {
        Id = l.Id, AddonId = l.AddonId.Value, NameSnapshot = l.NameSnapshot, Quantity = l.Quantity, UnitPriceCents = FirestoreValues.Cents(l.UnitPrice),
    };

    public AddonLine ToDomain() => AddonLine.Create(Id, new AddonId(AddonId), NameSnapshot, Quantity, FirestoreValues.Money(UnitPriceCents));
}
