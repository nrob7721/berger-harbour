using BergerHarbour.Domain.Bookings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class RoomingWarningAcceptanceDocument
{
    [FirestoreProperty("acceptedAt")]
    public Timestamp AcceptedAt { get; set; }

    [FirestoreProperty("warningTextShown")]
    public string WarningTextShown { get; set; } = string.Empty;

    public static RoomingWarningAcceptanceDocument From(RoomingWarningAcceptance r) =>
        new() { AcceptedAt = FirestoreValues.Instant(r.AcceptedAt), WarningTextShown = r.WarningTextShown };

    public RoomingWarningAcceptance ToDomain() => new(FirestoreValues.Instant(AcceptedAt), WarningTextShown);
}
