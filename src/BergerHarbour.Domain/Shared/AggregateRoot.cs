namespace BergerHarbour.Domain.Shared;

/// <summary>
/// Base class for every aggregate. <see cref="Version"/> is the persisted version the aggregate was loaded at
/// (0 for an aggregate that has never been saved) and drives optimistic concurrency.
/// </summary>
public abstract class AggregateRoot<TId> where TId : notnull
{
    protected AggregateRoot(TId id, int version)
    {
        Id = id;
        Version = version;
    }

    public TId Id { get; }

    public int Version { get; private set; }

    public bool IsNew => Version == 0;

    /// <summary>Throws <see cref="ConcurrencyConflictException"/> when the caller edited an older version.</summary>
    public void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException();
        }
    }

    /// <summary>Called by repositories after a successful write.</summary>
    public void MarkPersisted() => Version++;
}
