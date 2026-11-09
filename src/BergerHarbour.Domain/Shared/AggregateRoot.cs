namespace BergerHarbour.Domain.Shared;

public abstract class AggregateRoot
{
    /// <summary>Optimistic concurrency version. 0 = never saved.</summary>
    public int Version { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset ModifiedDate { get; private set; }

    public bool IsNew => Version == 0;

    protected void InitialiseAudit(DateTimeOffset now)
    {
        CreatedDate = now;
        ModifiedDate = now;
    }

    protected void RestoreAudit(int version, DateTimeOffset created, DateTimeOffset modified)
    {
        Version = version;
        CreatedDate = created;
        ModifiedDate = modified;
    }

    /// <summary>Throws when the caller edited an older version of this aggregate.</summary>
    public void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version) throw new ConcurrencyConflictException();
    }

    /// <summary>Called by repositories once a save is committed.</summary>
    public void MarkPersisted(DateTimeOffset now)
    {
        Version++;
        ModifiedDate = now;
    }
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}

public readonly record struct BoatId(string Value)
{
    public static BoatId New() => new(Ids.New());
    public override string ToString() => Value;
}
