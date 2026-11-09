using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Repositories;

public sealed class FirestoreBookingRepository : IBookingRepository
{
    private static readonly string[] OccupyingStatuses = [nameof(BookingStatus.Active), nameof(BookingStatus.PendingPayment)];

    private readonly CollectionReference collection;
    private readonly VersionedDocuments documents;

    internal FirestoreBookingRepository(FirestoreDb db, VersionedDocuments documents)
    {
        collection = db.Collection(FirestoreCollections.Bookings);
        this.documents = documents;
    }

    public async Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default) =>
        !VersionedDocuments.IsValidId(id.Value)
            ? null
            : (await documents.GetAsync<BookingDocument>(collection.Document(id.Value), ct))?.ToDomain(id.Value);

    public Task SaveAsync(Booking booking, CancellationToken ct = default) =>
        documents.SaveAsync(collection.Document(booking.Id.Value), booking, BookingDocument.From(booking), ct);

    /// <summary><c>endDate &gt; from</c> in the query (bounded by future bookings), <c>startDate &lt; to</c> in memory.</summary>
    public async Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var toText = FirestoreValues.Date(to);
        return (await QueryAsync(collection.WhereGreaterThan("endDate", FirestoreValues.Date(from)), ct))
            .Where(d => string.CompareOrdinal(d.Document.StartDate, toText) < 0)
            .Select(d => d.Document.ToDomain(d.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<Booking>> ListWithAddonAsync(AddonId addonId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var toText = FirestoreValues.Date(to);
        var query = collection.WhereArrayContains("addonIds", addonId.Value).WhereGreaterThan("endDate", FirestoreValues.Date(from));
        return (await QueryAsync(query, ct))
            .Where(d => string.CompareOrdinal(d.Document.StartDate, toText) < 0 && OccupyingStatuses.Contains(d.Document.Status))
            .Select(d => d.Document.ToDomain(d.Id))
            .ToList();
    }

    public async Task<IReadOnlyList<Booking>> ListByCustomerAsync(CustomerId customerId, CancellationToken ct = default) =>
        await ListAsync(collection.WhereEqualTo("customerId", customerId.Value), ct);

    public async Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) =>
        await ListAsync(collection.WhereEqualTo("status", status.ToString()), ct);

    public async Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly fromStartDate, CancellationToken ct = default) =>
        await ListAsync(
            collection.WhereEqualTo("status", nameof(BookingStatus.Active)).WhereGreaterThanOrEqualTo("startDate", FirestoreValues.Date(fromStartDate)),
            ct);

    public async Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        (await ListAsync(collection.WhereEqualTo("paymentLinkTokenHash", tokenHash).Limit(1), ct)).FirstOrDefault();

    public async Task<IReadOnlyList<Booking>> ListAllAsync(CancellationToken ct = default) => await ListAsync(collection, ct);

    private static async Task<IReadOnlyList<Booking>> ListAsync(Query query, CancellationToken ct) =>
        (await QueryAsync(query, ct)).Select(d => d.Document.ToDomain(d.Id)).ToList();

    private static async Task<IReadOnlyList<(string Id, BookingDocument Document)>> QueryAsync(Query query, CancellationToken ct) =>
        (await query.GetSnapshotAsync(ct)).Documents.Select(d => (d.Id, d.ConvertTo<BookingDocument>())).ToList();
}
