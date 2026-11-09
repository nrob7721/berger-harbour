using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class BoatUnavailabilityDocument : IVersionedDocument
{
    [FirestoreProperty("boatId")]
    public string BoatId { get; set; } = string.Empty;

    [FirestoreProperty("firstNight")]
    public string FirstNight { get; set; } = string.Empty;

    [FirestoreProperty("lastNight")]
    public string LastNight { get; set; } = string.Empty;

    [FirestoreProperty("comments")]
    public string? Comments { get; set; }

    [FirestoreProperty("createdDate")]
    public Timestamp CreatedDate { get; set; }

    [FirestoreProperty("modifiedDate")]
    public Timestamp ModifiedDate { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BoatUnavailabilityDocument From(BoatUnavailability u) => new()
    {
        BoatId = u.BoatId.Value,
        FirstNight = FirestoreValues.Date(u.Nights.FirstNight),
        LastNight = FirestoreValues.Date(u.Nights.LastNight),
        Comments = u.Comments,
        CreatedDate = FirestoreValues.Instant(u.CreatedDate),
        ModifiedDate = FirestoreValues.Instant(u.ModifiedDate),
        Version = u.Version,
    };

    public BoatUnavailability ToDomain(string id) => BoatUnavailability.Rehydrate(
        new BoatUnavailabilityId(id), new BoatId(BoatId), NightRange.Create(FirestoreValues.Date(FirstNight), FirestoreValues.Date(LastNight)), Comments,
        FirestoreValues.Instant(CreatedDate), FirestoreValues.Instant(ModifiedDate), Version);
}
