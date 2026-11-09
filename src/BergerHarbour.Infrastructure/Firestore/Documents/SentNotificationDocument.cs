using BergerHarbour.Domain.Bookings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class SentNotificationDocument
{
    [FirestoreProperty("key")]
    public string Key { get; set; } = string.Empty;

    [FirestoreProperty("sentAt")]
    public Timestamp SentAt { get; set; }

    public static SentNotificationDocument From(SentNotification n) => new() { Key = n.Key, SentAt = FirestoreValues.Instant(n.SentAt) };

    public SentNotification ToDomain() => new(Key, FirestoreValues.Instant(SentAt));
}
