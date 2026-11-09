using BergerHarbour.Domain.Settings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class SeasonDocument : IVersionedDocument
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty("isDefault")]
    public bool IsDefault { get; set; }

    [FirestoreProperty("ranges")]
    public List<SeasonRangeDocument> Ranges { get; set; } = [];

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static SeasonDocument From(Season s) =>
        new() { Name = s.Name, IsDefault = s.IsDefault, Ranges = s.Ranges.Select(SeasonRangeDocument.From).ToList(), Version = s.Version };

    public Season ToDomain(string id) => Season.Rehydrate(new SeasonId(id), Name, IsDefault, Ranges.Select(r => r.ToDomain()).ToList(), Version);
}
