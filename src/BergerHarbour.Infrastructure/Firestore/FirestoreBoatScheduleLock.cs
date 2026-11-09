using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// The per-boat exclusive section: a Firestore transaction that first reads and then updates <c>boatLocks/{boatId}</c>.
/// Concurrent transactions on the same boat touch the same lock document, so Firestore serialises them (one retries and
/// re-reads). Writes are buffered and committed together at the end, because Firestore transactions read before they write.
/// </summary>
public sealed class FirestoreBoatScheduleLock(FirestoreDb db, IClock clock) : IBoatScheduleLock
{
    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default)
    {
        Session? committed = null;
        var result = await db.RunTransactionAsync(async tx =>
        {
            var lockRef = db.Collection(Collections.BoatLocks).Document(boatId.Value);
            var lockSnap = await tx.GetSnapshotAsync(lockRef, ct);
            var counter = lockSnap.Exists && lockSnap.TryGetValue<long>("counter", out var c) ? c : 0L;

            var session = new Session(db, tx, boatId, ct);
            var value = await work(session);
            var now = clock.UtcNow;
            tx.Set(lockRef, new Dictionary<string, object> { ["counter"] = counter + 1, ["updatedAt"] = Timestamp.FromDateTimeOffset(now) });
            session.Flush(now);
            committed = session;
            return value;
        }, cancellationToken: ct);
        committed?.MarkPersisted(clock.UtcNow);
        return result;
    }

    private sealed class Session(FirestoreDb db, Transaction tx, BoatId boatId, CancellationToken ct) : IBoatScheduleSession
    {
        private readonly Dictionary<string, Booking> _bookings = new();
        private readonly Dictionary<string, BoatUnavailability> _unavailabilities = new();
        /// <summary>Versions as read in this transaction, for the optimistic check on write.</summary>
        private readonly Dictionary<string, int> _readVersions = new();

        public BoatId BoatId => boatId;

        public async Task<IReadOnlyList<Booking>> GetPotentiallyOverlappingBookingsAsync(StayRange stay)
        {
            var query = db.Collection(Collections.Bookings)
                .WhereEqualTo("boatId", boatId.Value)
                .WhereIn("status", BookingStatuses.Blocking.Select(s => s.ToString()).ToArray())
                .WhereLessThan("startDate", Dates.Iso(stay.End));
            var snap = await tx.GetSnapshotAsync(query, ct);
            return snap.Documents.Select(d => Track(d.ConvertTo<BookingDoc>().ToDomain(d.Id))).Where(b => b.EndDate > stay.Start).ToList();
        }

        public async Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(StayRange stay)
        {
            var query = db.Collection(Collections.BoatUnavailabilities)
                .WhereEqualTo("boatId", boatId.Value)
                .WhereLessThan("firstNight", Dates.Iso(stay.End));
            var snap = await tx.GetSnapshotAsync(query, ct);
            return snap.Documents.Select(d => d.ConvertTo<UnavailabilityDoc>().ToDomain(d.Id))
                .Where(u => u.Nights.LastNight >= stay.Start).Select(u => { _readVersions["u:" + u.Id] = u.Version; return u; }).ToList();
        }

        public async Task<Booking?> GetBookingAsync(string bookingId)
        {
            var snap = await tx.GetSnapshotAsync(db.Collection(Collections.Bookings).Document(bookingId), ct);
            return snap.Exists ? Track(snap.ConvertTo<BookingDoc>().ToDomain(snap.Id)) : null;
        }

        public async Task<BoatUnavailability?> GetUnavailabilityAsync(string unavailabilityId)
        {
            var snap = await tx.GetSnapshotAsync(db.Collection(Collections.BoatUnavailabilities).Document(unavailabilityId), ct);
            if (!snap.Exists) return null;
            var u = snap.ConvertTo<UnavailabilityDoc>().ToDomain(snap.Id);
            _readVersions["u:" + u.Id] = u.Version;
            return u;
        }

        private Booking Track(Booking b)
        {
            _readVersions["b:" + b.Id] = b.Version;
            return b;
        }

        public void SaveBooking(Booking booking) => _bookings[booking.Id] = booking;
        public void SaveUnavailability(BoatUnavailability unavailability) => _unavailabilities[unavailability.Id] = unavailability;

        public void Flush(DateTimeOffset now)
        {
            foreach (var b in _bookings.Values)
            {
                CheckVersion("b:" + b.Id, b.Version);
                var doc = BookingDoc.From(b);
                doc.Version = b.Version + 1;
                doc.ModifiedDate = now;
                var reference = db.Collection(Collections.Bookings).Document(b.Id);
                if (b.IsNew) tx.Create(reference, doc); else tx.Set(reference, doc);
            }
            foreach (var u in _unavailabilities.Values)
            {
                CheckVersion("u:" + u.Id, u.Version);
                var doc = UnavailabilityDoc.From(u);
                doc.Version = u.Version + 1;
                doc.ModifiedDate = now;
                var reference = db.Collection(Collections.BoatUnavailabilities).Document(u.Id);
                if (u.IsNew) tx.Create(reference, doc); else tx.Set(reference, doc);
            }
        }

        /// <summary>An existing aggregate must have been read in this transaction with the same version.</summary>
        private void CheckVersion(string key, int version)
        {
            if (version == 0) return;
            if (!_readVersions.TryGetValue(key, out var read) || read != version) throw new ConcurrencyConflictException();
        }

        public void MarkPersisted(DateTimeOffset now)
        {
            foreach (var b in _bookings.Values) b.MarkPersisted(now);
            foreach (var u in _unavailabilities.Values) u.MarkPersisted(now);
        }
    }
}

/// <summary>One counter document per reference prefix, incremented in a transaction so suffixes are unique.</summary>
public sealed class FirestoreBookingReferenceGenerator(FirestoreDb db) : IBookingReferenceGenerator
{
    public Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var reference = db.Collection(Collections.Counters).Document("ref-" + prefix);
            var snap = await tx.GetSnapshotAsync(reference, ct);
            var next = snap.Exists && snap.TryGetValue<long>("next", out var n) ? n : 0L;
            tx.Set(reference, new Dictionary<string, object> { ["next"] = next + 1 });
            return (int)next;
        }, cancellationToken: ct);
}
