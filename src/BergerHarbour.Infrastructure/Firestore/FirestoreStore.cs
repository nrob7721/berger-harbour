using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

public static class Collections
{
    public const string Boats = "boats";
    public const string Bookings = "bookings";
    public const string BookingDrafts = "bookingDrafts";
    public const string Customers = "customers";
    public const string CustomerEmails = "customerEmails";
    public const string BoatUnavailabilities = "boatUnavailabilities";
    public const string BoatLocks = "boatLocks";
    public const string Seasons = "seasons";
    public const string BlockedPeriods = "blockedPeriods";
    public const string Addons = "addons";
    public const string EmailTemplates = "emailTemplates";
    public const string Settings = "settings";
    public const string Counters = "counters";
}

/// <summary>Versioned reads and writes shared by the repositories.</summary>
public sealed class FirestoreStore(FirestoreDb db, IClock clock)
{
    public FirestoreDb Db => db;
    public IClock Clock => clock;

    public CollectionReference Collection(string name) => db.Collection(name);

    public async Task<T?> GetAsync<TDoc, T>(string collection, string id, Func<TDoc, string, T> map, CancellationToken ct) where TDoc : class where T : class
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains('/')) return null;
        var snap = await db.Collection(collection).Document(id).GetSnapshotAsync(ct);
        return snap.Exists ? map(snap.ConvertTo<TDoc>(), snap.Id) : null;
    }

    public static async Task<IReadOnlyList<T>> QueryAsync<TDoc, T>(Query query, Func<TDoc, string, T> map, CancellationToken ct) where TDoc : class
    {
        var snap = await query.GetSnapshotAsync(ct);
        return snap.Documents.Select(d => map(d.ConvertTo<TDoc>(), d.Id)).ToList();
    }

    /// <summary>
    /// Saves an aggregate if the stored version still matches the one it was loaded with; otherwise throws
    /// <see cref="ConcurrencyConflictException"/>. Writes version + 1 and marks the aggregate persisted after the commit.
    /// </summary>
    public async Task SaveAsync<TDoc>(string collection, string id, AggregateRoot aggregate, Func<TDoc> toDoc, CancellationToken ct,
        Action<Transaction>? alsoWrite = null, Func<Transaction, Task>? alsoRead = null) where TDoc : Doc
    {
        var now = clock.UtcNow;
        var expected = aggregate.Version;
        var reference = db.Collection(collection).Document(id);
        await db.RunTransactionAsync(async tx =>
        {
            var snap = await tx.GetSnapshotAsync(reference, ct);
            var stored = snap.Exists ? snap.GetValue<int>("version") : 0;
            if (stored != expected) throw new ConcurrencyConflictException();
            if (alsoRead is not null) await alsoRead(tx);
            var doc = toDoc();
            doc.Version = expected + 1;
            doc.ModifiedDate = now;
            tx.Set(reference, doc);
            alsoWrite?.Invoke(tx);
        }, cancellationToken: ct);
        aggregate.MarkPersisted(now);
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken ct) =>
        await db.Collection(collection).Document(id).DeleteAsync(cancellationToken: ct);
}
