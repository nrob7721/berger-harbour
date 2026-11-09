namespace BergerHarbour.Domain.Shared;

/// <summary>
/// Base of every aggregate. <see cref="Version"/> drives optimistic concurrency: a new aggregate has version 0, and the
/// repository sets the stored version after each successful write.
/// </summary>
public abstract class AggregateRoot
{
    public int Version { get; private set; }

    /// <summary>Called by repositories when the aggregate is loaded or has been written.</summary>
    public void SetPersistedVersion(int version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Version = version;
    }

    /// <summary>Throws <see cref="ConcurrencyException"/> unless the caller edited the version this aggregate is at.</summary>
    public void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyException();
        }
    }
}
