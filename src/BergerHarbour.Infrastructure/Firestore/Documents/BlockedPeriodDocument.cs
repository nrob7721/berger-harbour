using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BlockedPeriodDocument : IVersionedDocument
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty("firstNight")]
    public string FirstNight { get; set; } = string.Empty;

    [FirestoreProperty("lastNight")]
    public string LastNight { get; set; } = string.Empty;

    [FirestoreProperty("usesExtendedPaymentSchedule")]
    public bool UsesExtendedPaymentSchedule { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BlockedPeriodDocument From(BlockedPeriod p) => new()
    {
        Name = p.Name,
        FirstNight = FirestoreValues.Date(p.Nights.FirstNight),
        LastNight = FirestoreValues.Date(p.Nights.LastNight),
        UsesExtendedPaymentSchedule = p.UsesExtendedPaymentSchedule,
        Version = p.Version,
    };

    public BlockedPeriod ToDomain(string id) => BlockedPeriod.Rehydrate(
        new BlockedPeriodId(id), Name, NightRange.Create(FirestoreValues.Date(FirstNight), FirestoreValues.Date(LastNight)), UsesExtendedPaymentSchedule,
        Version);
}
