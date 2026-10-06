using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreBookingRepository(FirestoreDb db) : IBookingRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.Bookings);

    public async Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default)
    {
        var snapshot = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return snapshot.Exists ? ToBooking(snapshot.Id, snapshot.ConvertTo<BookingDocument>()) : null;
    }

    public Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken ct = default) =>
        FirstAsync(Collection.WhereEqualTo("reference", reference.Value).Limit(1), ct);

    public Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        FirstAsync(Collection.WhereEqualTo("paymentLinkTokenHash", tokenHash).Limit(1), ct);

    /// <summary>Queries startDate &lt; to (plus status and boat filters) and filters endDate &gt; from in memory.</summary>
    public async Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<BookingStatus>? statuses = null, BoatId? boatId = null, CancellationToken ct = default)
    {
        Query query = Collection;
        if (boatId is { } boat)
        {
            query = query.WhereEqualTo("boatId", boat.Value);
        }

        if (statuses is { Count: > 0 })
        {
            query = query.WhereIn("status", statuses.Select(s => s.ToString()));
        }

        query = query.WhereLessThan("startDate", Date(to));
        var fromKey = Date(from);
        var snapshot = await query.GetSnapshotAsync(ct);
        return snapshot.Documents
            .Select(d => (d.Id, Doc: d.ConvertTo<BookingDocument>()))
            .Where(x => string.CompareOrdinal(x.Doc.EndDate, fromKey) > 0)
            .Select(x => ToBooking(x.Id, x.Doc))
            .ToList();
    }

    public async Task<IReadOnlyList<Booking>> ListByCustomerAsync(IReadOnlyCollection<CustomerId> customerIds,
        CancellationToken ct = default)
    {
        var result = new List<Booking>();
        foreach (var chunk in customerIds.Select(c => c.Value).Distinct().Chunk(30))
        {
            result.AddRange(await ListAsync(Collection.WhereIn("customerId", chunk), ct));
        }

        return result;
    }

    public Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) =>
        ListAsync(Collection.WhereEqualTo("status", status.ToString()), ct);

    public Task<IReadOnlyList<Booking>> ListByStatusStartingFromAsync(BookingStatus status, DateOnly startDateFrom,
        CancellationToken ct = default) =>
        ListAsync(Collection.WhereEqualTo("status", status.ToString()).WhereGreaterThanOrEqualTo("startDate", Date(startDateFrom)), ct);

    public async Task SaveAsync(Booking booking, CancellationToken ct = default)
    {
        var snapshot = booking.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(snapshot.Id), snapshot.Version,
            ToDocument(snapshot, snapshot.Version + 1), ct);
        booking.MarkPersisted();
    }

    private static async Task<Booking?> FirstAsync(Query query, CancellationToken ct) =>
        (await ListAsync(query, ct)).FirstOrDefault();

    private static async Task<IReadOnlyList<Booking>> ListAsync(Query query, CancellationToken ct)
    {
        var snapshot = await query.GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => ToBooking(d.Id, d.ConvertTo<BookingDocument>())).ToList();
    }
}
