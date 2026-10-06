using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>Per-year sequence in counters/bookings-{yyyy}, incremented transactionally.</summary>
public sealed class FirestoreBookingReferenceGenerator(FirestoreDb db) : IBookingReferenceGenerator
{
    public async Task<BookingReference> NextAsync(int year, CancellationToken ct = default)
    {
        var reference = db.Collection(FirestoreCollections.Counters).Document($"bookings-{year}");
        var next = await VersionedWriter.RunAsync(db, async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            var value = (snapshot.Exists && snapshot.TryGetValue<long>("next", out var n) ? n : 0) + 1;
            tx.Set(reference, new Dictionary<string, object> { ["next"] = value });
            return value;
        }, ct);
        return BookingReference.Create(year, next);
    }
}
