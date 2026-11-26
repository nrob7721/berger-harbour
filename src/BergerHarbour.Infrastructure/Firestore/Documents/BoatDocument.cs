using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BoatDocument : IVersionedDocument
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty("slug")]
    public string Slug { get; set; } = string.Empty;

    [FirestoreProperty("type")]
    public string Type { get; set; } = nameof(BoatType.House);

    [FirestoreProperty("berth")]
    public int Berth { get; set; }

    [FirestoreProperty("adultBeds")]
    public int? AdultBeds { get; set; }

    [FirestoreProperty("kidsBeds")]
    public int? KidsBeds { get; set; }

    [FirestoreProperty("beddingDescription")]
    public string? BeddingDescription { get; set; }

    [FirestoreProperty("securityBondCents")]
    public long SecurityBondCents { get; set; }

    [FirestoreProperty("depositAmountCents")]
    public long? DepositAmountCents { get; set; }

    /// <summary>The photos in staff order. Boats saved before v1.5 had a single <c>imageUrl</c> instead; it is ignored (the seed fills photos in, §13).</summary>
    [FirestoreProperty("images")]
    public List<BoatImageDocument> Images { get; set; } = [];

    [FirestoreProperty("tags")]
    public List<string> Tags { get; set; } = [];

    [FirestoreProperty("hasSpa")]
    public bool HasSpa { get; set; }

    [FirestoreProperty("isPetFriendly")]
    public bool IsPetFriendly { get; set; }

    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; }

    [FirestoreProperty("rates")]
    public List<BoatRateDocument> Rates { get; set; } = [];

    [FirestoreProperty("allowedAddonIds")]
    public List<string> AllowedAddonIds { get; set; } = [];

    [FirestoreProperty("createdDate")]
    public Timestamp CreatedDate { get; set; }

    [FirestoreProperty("modifiedDate")]
    public Timestamp ModifiedDate { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BoatDocument From(Boat b) => new()
    {
        Name = b.Name,
        Slug = b.Slug,
        Type = b.Type.ToString(),
        Berth = b.Berth,
        AdultBeds = b.AdultBeds,
        KidsBeds = b.KidsBeds,
        BeddingDescription = b.BeddingDescription,
        SecurityBondCents = FirestoreValues.Cents(b.SecurityBond),
        DepositAmountCents = FirestoreValues.Cents(b.DepositAmount),
        Images = b.Images.Select(BoatImageDocument.From).ToList(),
        Tags = b.Tags.ToList(),
        HasSpa = b.HasSpa,
        IsPetFriendly = b.IsPetFriendly,
        IsActive = b.IsActive,
        Rates = b.Rates.Select(BoatRateDocument.From).ToList(),
        AllowedAddonIds = b.AllowedAddonIds.Select(a => a.Value).ToList(),
        CreatedDate = FirestoreValues.Instant(b.CreatedDate),
        ModifiedDate = FirestoreValues.Instant(b.ModifiedDate),
        Version = b.Version,
    };

    public Boat ToDomain(string id) => Boat.Rehydrate(
        new BoatId(id),
        new BoatDetails(Name, Slug, Enum.Parse<BoatType>(Type), Berth, AdultBeds, KidsBeds, BeddingDescription, FirestoreValues.Money(SecurityBondCents),
            FirestoreValues.Money(DepositAmountCents), Images.Select(i => i.ToDomain()).ToList(), Tags.ToList(), HasSpa, IsPetFriendly, Rates.Select(r => r.ToDomain()).ToList(),
            AllowedAddonIds.Select(a => new AddonId(a)).ToList()),
        IsActive,
        FirestoreValues.Instant(CreatedDate),
        FirestoreValues.Instant(ModifiedDate),
        Version);
}
