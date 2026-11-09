using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// Transaction-bound reads and queued writes for one boat. Firestore needs every read before any write, so writes are
/// applied in <see cref="FlushAsync"/> after their version checks.
/// </summary>
internal sealed class FirestoreBoatScheduleSession : IBoatScheduleSession
{
    private static readonly string[] OccupyingStatuses = [nameof(BookingStatus.Active), nameof(BookingStatus.PendingPayment)];

    private readonly Transaction tx;
    private readonly CollectionReference bookings;
    private readonly CollectionReference unavailabilities;
    private readonly Dictionary<string, int> versionsRead = new(StringComparer.Ordinal);
    private readonly List<(DocumentReference Reference, AggregateRoot Aggregate, IVersionedDocument? Document)> writes = [];

    public FirestoreBoatScheduleSession(FirestoreDb db, Transaction tx, BoatId boatId)
    {
        this.tx = tx;
        BoatId = boatId;
        bookings = db.Collection(FirestoreCollections.Bookings);
        unavailabilities = db.Collection(FirestoreCollections.BoatUnavailabilities);
    }

    public BoatId BoatId { get; }

    public async Task<Booking?> GetBookingAsync(BookingId id)
    {
        if (!VersionedDocuments.IsValidId(id.Value))
        {
            return null;
        }

        var snapshot = await tx.GetSnapshotAsync(bookings.Document(id.Value));
        Remember(snapshot);
        return snapshot.Exists ? snapshot.ConvertTo<BookingDocument>().ToDomain(snapshot.Id) : null;
    }

    /// <summary><c>boatId == X</c>, <c>status in [Active, PendingPayment]</c>, <c>startDate &lt; end</c>; then <c>endDate &gt; start</c> in memory.</summary>
    public async Task<IReadOnlyList<Booking>> ListOverlappingBookingsAsync(StayRange stay)
    {
        var query = bookings.WhereEqualTo("boatId", BoatId.Value).WhereIn("status", OccupyingStatuses)
            .WhereLessThan("startDate", FirestoreValues.Date(stay.End));
        var snapshot = await tx.GetSnapshotAsync(query);
        var start = FirestoreValues.Date(stay.Start);
        return snapshot.Documents
            .Select(d =>
            {
                Remember(d);
                return (d.Id, Document: d.ConvertTo<BookingDocument>());
            })
            .Where(d => string.CompareOrdinal(d.Document.EndDate, start) > 0)
            .Select(d => d.Document.ToDomain(d.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<BoatUnavailability>> ListOverlappingUnavailabilitiesAsync(StayRange stay)
    {
        var query = unavailabilities.WhereEqualTo("boatId", BoatId.Value).WhereLessThanOrEqualTo("firstNight", FirestoreValues.Date(stay.LastNight));
        var snapshot = await tx.GetSnapshotAsync(query);
        return snapshot.Documents
            .Select(d =>
            {
                Remember(d);
                return d.ConvertTo<BoatUnavailabilityDocument>().ToDomain(d.Id);
            })
            .Where(u => u.Nights.Overlaps(stay))
            .ToList();
    }

    public async Task<BoatUnavailability?> GetUnavailabilityAsync(BoatUnavailabilityId id)
    {
        if (!VersionedDocuments.IsValidId(id.Value))
        {
            return null;
        }

        var snapshot = await tx.GetSnapshotAsync(unavailabilities.Document(id.Value));
        Remember(snapshot);
        return snapshot.Exists ? snapshot.ConvertTo<BoatUnavailabilityDocument>().ToDomain(snapshot.Id) : null;
    }

    public void SaveBooking(Booking booking) => Queue(bookings.Document(booking.Id.Value), booking, BookingDocument.From(booking));

    public void SaveUnavailability(BoatUnavailability unavailability) =>
        Queue(unavailabilities.Document(unavailability.Id.Value), unavailability, BoatUnavailabilityDocument.From(unavailability));

    public void DeleteUnavailability(BoatUnavailability unavailability) =>
        Queue(unavailabilities.Document(unavailability.Id.Value), unavailability, null);

    /// <summary>Checks every queued write's version (reading the documents not read yet), then writes.</summary>
    public async Task FlushAsync()
    {
        foreach (var (reference, aggregate, _) in writes)
        {
            if (!versionsRead.TryGetValue(reference.Path, out var stored))
            {
                var snapshot = await tx.GetSnapshotAsync(reference);
                Remember(snapshot);
                stored = versionsRead[reference.Path];
            }

            var exists = stored >= 0;
            if (exists ? stored != aggregate.Version || aggregate.Version == 0 : aggregate.Version != 0)
            {
                throw new ConcurrencyException();
            }
        }

        foreach (var (reference, aggregate, document) in writes)
        {
            if (document is null)
            {
                tx.Delete(reference);
            }
            else
            {
                document.Version = aggregate.Version + 1;
                tx.Set(reference, document);
            }
        }
    }

    /// <summary>After the transaction commits, the aggregates carry their new versions.</summary>
    public void ApplyPersistedVersions()
    {
        foreach (var (_, aggregate, document) in writes)
        {
            if (document is not null)
            {
                aggregate.SetPersistedVersion(document.Version);
            }
        }
    }

    private void Queue(DocumentReference reference, AggregateRoot aggregate, IVersionedDocument? document)
    {
        writes.RemoveAll(w => w.Reference.Path == reference.Path);
        writes.Add((reference, aggregate, document));
    }

    /// <summary>Records the version this transaction read (−1 when the document doesn't exist).</summary>
    private void Remember(DocumentSnapshot snapshot) =>
        versionsRead[snapshot.Reference.Path] = snapshot.Exists ? snapshot.GetValue<int>("version") : -1;
}
