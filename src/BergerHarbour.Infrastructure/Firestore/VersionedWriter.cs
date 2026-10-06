using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>Optimistic concurrency: a write only succeeds when the stored version is the one the aggregate was loaded at.</summary>
internal static class VersionedWriter
{
    /// <summary>
    /// Inserts (loadedVersion 0) or updates a document with a version check inside a transaction. The document is
    /// written with version loadedVersion + 1. <paramref name="alsoWrite"/> adds writes to the same transaction
    /// after its reads.
    /// </summary>
    public static Task SaveAsync<TDocument>(FirestoreDb db, DocumentReference reference, int loadedVersion,
        TDocument document, CancellationToken ct, Func<Transaction, Task<Action>>? alsoWrite = null) where TDocument : class =>
        RunAsync(db, async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            EnsureVersion(snapshot, loadedVersion);
            var extraWrites = alsoWrite is null ? null : await alsoWrite(tx);
            Write(tx, reference, loadedVersion, document);
            extraWrites?.Invoke();
            return true;
        }, ct);

    /// <summary>
    /// Runs a transaction; when Firestore gives up because of contention (ABORTED after all attempts) the caller
    /// gets a <see cref="ConcurrencyConflictException"/>, which use cases already know how to retry or report.
    /// </summary>
    public static async Task<T> RunAsync<T>(FirestoreDb db, Func<Transaction, Task<T>> work, CancellationToken ct,
        int maxAttempts = 10)
    {
        // Each round lets the client retry a couple of times; between rounds we back off with jitter so that
        // contending transactions stop retrying in lock-step.
        const int attemptsPerRound = 2;
        var rounds = Math.Max(1, maxAttempts / attemptsPerRound);
        for (var round = 1; ; round++)
        {
            try
            {
                return await db.RunTransactionAsync(work, TransactionOptions.ForMaxAttempts(attemptsPerRound), ct);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Aborted)
            {
                if (round >= rounds)
                {
                    throw new ConcurrencyConflictException();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(25, 150) * round), ct);
            }
        }
    }

    public static void EnsureVersion(DocumentSnapshot snapshot, int loadedVersion)
    {
        if (loadedVersion == 0)
        {
            if (snapshot.Exists)
            {
                throw new ConcurrencyConflictException();
            }

            return;
        }

        if (!snapshot.Exists || !snapshot.TryGetValue<int>("version", out var stored) || stored != loadedVersion)
        {
            throw new ConcurrencyConflictException();
        }
    }

    public static void Write<TDocument>(Transaction tx, DocumentReference reference, int loadedVersion, TDocument document)
        where TDocument : class
    {
        if (loadedVersion == 0)
        {
            tx.Create(reference, document);
        }
        else
        {
            tx.Set(reference, document);
        }
    }

    public static Task DeleteAsync(FirestoreDb db, DocumentReference reference, int loadedVersion, CancellationToken ct) =>
        RunAsync(db, async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            EnsureVersion(snapshot, loadedVersion);
            tx.Delete(reference);
            return true;
        }, ct);
}
