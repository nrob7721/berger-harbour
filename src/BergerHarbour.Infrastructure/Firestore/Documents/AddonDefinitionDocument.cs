using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class AddonDefinitionDocument : IVersionedDocument
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty("description")]
    public string? Description { get; set; }

    [FirestoreProperty("priceOnRequest")]
    public bool PriceOnRequest { get; set; }

    [FirestoreProperty("midweekPriceCents")]
    public long? MidweekPriceCents { get; set; }

    [FirestoreProperty("weekendPriceCents")]
    public long? WeekendPriceCents { get; set; }

    [FirestoreProperty("weekPriceCents")]
    public long? WeekPriceCents { get; set; }

    [FirestoreProperty("quantityApplies")]
    public bool QuantityApplies { get; set; }

    [FirestoreProperty("capacity")]
    public int? Capacity { get; set; }

    [FirestoreProperty("capacityUnitLabel")]
    public string? CapacityUnitLabel { get; set; }

    [FirestoreProperty("sortOrder")]
    public int SortOrder { get; set; }

    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static AddonDefinitionDocument From(AddonDefinition a) => new()
    {
        Name = a.Name,
        Description = a.Description,
        PriceOnRequest = a.PriceOnRequest,
        MidweekPriceCents = FirestoreValues.Cents(a.Prices.Midweek),
        WeekendPriceCents = FirestoreValues.Cents(a.Prices.Weekend),
        WeekPriceCents = FirestoreValues.Cents(a.Prices.Week),
        QuantityApplies = a.QuantityApplies,
        Capacity = a.Capacity,
        CapacityUnitLabel = a.CapacityUnitLabel,
        SortOrder = a.SortOrder,
        IsActive = a.IsActive,
        Version = a.Version,
    };

    public AddonDefinition ToDomain(string id) => AddonDefinition.Rehydrate(
        new AddonId(id), Name, Description, PriceOnRequest,
        new AddonPrices(FirestoreValues.Money(MidweekPriceCents), FirestoreValues.Money(WeekendPriceCents), FirestoreValues.Money(WeekPriceCents)),
        QuantityApplies, Capacity, CapacityUnitLabel, SortOrder, IsActive, Version);
}
