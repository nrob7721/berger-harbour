using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// The per-boat exclusive section on Firestore: a transaction that first reads and then updates
/// boatLocks/{boatId}. Because every occupancy write for a boat reads and writes that document, concurrent
/// transactions on the same boat are serialised (Firestore aborts and retries the loser, which then sees the
/// winner's booking). A relational implementation would use SELECT … FOR UPDATE on the boat row instead.
/// </summary>
public sealed class FirestoreBoatScheduleLock(FirestoreDb db) : IBoatScheduleLock
{
    public const int MaxAttempts = 10;

    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default)
    {
        Session? committed = null;
        var result = await VersionedWriter.RunAsync(db, async tx =>
        {
            var session = new Session(db, tx, boatId, ct);
            await session.LockAsync();
            var value = await work(session);
            await session.CommitAsync();
            committed = session;
            return value;
        }, ct, MaxAttempts);

        committed?.MarkPersisted();
        return result;
    }

    private sealed class Session(FirestoreDb db, Transaction tx, BoatId boatId, CancellationToken ct) : IBoatScheduleSession
    {
        private readonly Dictionary<string, (Booking Booking, int StoredVersion)> _bookings = new();
        private readonly Dictionary<string, (BoatUnavailability Unavailability, int StoredVersion)> _unavailabilities = new();
        private readonly List<Booking> _pendingBookings = [];
        private readonly List<BoatUnavailability> _pendingUnavailabilities = [];
        private long _lockCounter;

        public BoatId BoatId => boatId;

        private DocumentReference LockReference => db.Collection(FirestoreCollections.BoatLocks).Document(boatId.Value);

        private CollectionReference Bookings => db.Collection(FirestoreCollections.Bookings);

        private CollectionReference Unavailabilities => db.Collection(FirestoreCollections.BoatUnavailabilities);

        public async Task LockAsync()
        {
            var snapshot = await tx.GetSnapshotAsync(LockReference, ct);
            _lockCounter = snapshot.Exists && snapshot.TryGetValue<long>("counter", out var counter) ? counter : 0;
        }

        public async Task<Booking?> GetBookingAsync(BookingId id)
        {
            if (_bookings.TryGetValue(id.Value, out var tracked))
            {
                return tracked.Booking;
            }

            var snapshot = await tx.GetSnapshotAsync(Bookings.Document(id.Value), ct);
            return snapshot.Exists ? Track(snapshot.Id, snapshot.ConvertTo<BookingDocument>()) : null;
        }

        public async Task<IReadOnlyList<Booking>> GetOccupyingBookingsAsync(Stay range)
        {
            var query = Bookings.WhereEqualTo("boatId", boatId.Value)
                .WhereIn("status", new[] { BookingStatus.Active.ToString(), BookingStatus.PendingPayment.ToString() })
                .WhereLessThan("startDate", Date(range.EndDate));
            var snapshot = await tx.GetSnapshotAsync(query, ct);
            var startKey = Date(range.StartDate);
            return snapshot.Documents.Select(d => (d.Id, Doc: d.ConvertTo<BookingDocument>()))
                .Where(x => string.CompareOrdinal(x.Doc.EndDate, startKey) > 0)
                .Select(x => Track(x.Id, x.Doc))
                .ToList();
        }

        public async Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(Stay range)
        {
            var query = Unavailabilities.WhereEqualTo("boatId", boatId.Value).WhereLessThan("firstNight", Date(range.EndDate));
            var snapshot = await tx.GetSnapshotAsync(query, ct);
            var startKey = Date(range.StartDate);
            return snapshot.Documents.Select(d => (d.Id, Doc: d.ConvertTo<UnavailabilityDocument>()))
                .Where(x => string.CompareOrdinal(x.Doc.LastNight, startKey) >= 0)
                .Select(x => Track(x.Id, x.Doc))
                .ToList();
        }

        public async Task<BoatUnavailability?> GetUnavailabilityAsync(UnavailabilityId id)
        {
            if (_unavailabilities.TryGetValue(id.Value, out var tracked))
            {
                return tracked.Unavailability;
            }

            var snapshot = await tx.GetSnapshotAsync(Unavailabilities.Document(id.Value), ct);
            return snapshot.Exists ? Track(snapshot.Id, snapshot.ConvertTo<UnavailabilityDocument>()) : null;
        }

        public void Save(Booking booking)
        {
            if (!_pendingBookings.Contains(booking))
            {
                _pendingBookings.Add(booking);
            }
        }

        public void Save(BoatUnavailability unavailability)
        {
            if (!_pendingUnavailabilities.Contains(unavailability))
            {
                _pendingUnavailabilities.Add(unavailability);
            }
        }

        /// <summary>Checks versions, then writes the lock counter and every queued aggregate.</summary>
        public async Task CommitAsync()
        {
            // Reads must precede writes in a Firestore transaction, so check untracked updates first.
            foreach (var booking in _pendingBookings.Where(b => !b.IsNew && !_bookings.ContainsKey(b.Id.Value)))
            {
                VersionedWriter.EnsureVersion(await tx.GetSnapshotAsync(Bookings.Document(booking.Id.Value), ct), booking.Version);
            }

            foreach (var u in _pendingUnavailabilities.Where(u => !u.IsNew && !_unavailabilities.ContainsKey(u.Id.Value)))
            {
                VersionedWriter.EnsureVersion(await tx.GetSnapshotAsync(Unavailabilities.Document(u.Id.Value), ct), u.Version);
            }

            foreach (var booking in _pendingBookings)
            {
                if (_bookings.TryGetValue(booking.Id.Value, out var tracked) && tracked.StoredVersion != booking.Version)
                {
                    throw new ConcurrencyConflictException();
                }

                var s = booking.ToSnapshot();
                VersionedWriter.Write(tx, Bookings.Document(s.Id), s.Version, ToDocument(s, s.Version + 1));
            }

            foreach (var u in _pendingUnavailabilities)
            {
                if (_unavailabilities.TryGetValue(u.Id.Value, out var tracked) && tracked.StoredVersion != u.Version)
                {
                    throw new ConcurrencyConflictException();
                }

                var s = u.ToSnapshot();
                VersionedWriter.Write(tx, Unavailabilities.Document(s.Id), s.Version, ToDocument(s, s.Version + 1));
            }

            tx.Set(LockReference, new Dictionary<string, object>
            {
                ["counter"] = _lockCounter + 1,
                ["updatedAt"] = Timestamp.GetCurrentTimestamp(),
            });
        }

        public void MarkPersisted()
        {
            foreach (var booking in _pendingBookings)
            {
                booking.MarkPersisted();
            }

            foreach (var u in _pendingUnavailabilities)
            {
                u.MarkPersisted();
            }
        }

        private Booking Track(string id, BookingDocument document)
        {
            if (_bookings.TryGetValue(id, out var tracked))
            {
                return tracked.Booking;
            }

            var booking = ToBooking(id, document);
            _bookings[id] = (booking, document.Version);
            return booking;
        }

        private BoatUnavailability Track(string id, UnavailabilityDocument document)
        {
            if (_unavailabilities.TryGetValue(id, out var tracked))
            {
                return tracked.Unavailability;
            }

            var unavailability = ToUnavailability(id, document);
            _unavailabilities[id] = (unavailability, document.Version);
            return unavailability;
        }
    }
}
