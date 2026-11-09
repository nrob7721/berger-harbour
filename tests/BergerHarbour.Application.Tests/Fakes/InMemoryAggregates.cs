using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>
/// A versioned in-memory table shared by the fake repositories. Rows are rehydrated copies, so tests see the same
/// isolation a database gives.
/// </summary>
public sealed class InMemoryAggregates<TId, T>
    where TId : notnull
    where T : AggregateRoot
{
    private readonly Dictionary<TId, T> rows = [];
    private readonly Func<T, T> copy;
    private readonly Func<T, TId> idOf;

    public InMemoryAggregates(Func<T, TId> idOf, Func<T, T> copy)
    {
        this.idOf = idOf;
        this.copy = copy;
    }

    public IReadOnlyList<T> All
    {
        get
        {
            lock (rows)
            {
                return rows.Values.Select(copy).ToList();
            }
        }
    }

    public T? Get(TId id)
    {
        lock (rows)
        {
            return rows.TryGetValue(id, out var row) ? copy(row) : null;
        }
    }

    public void Save(T aggregate)
    {
        lock (rows)
        {
            var id = idOf(aggregate);
            var stored = rows.TryGetValue(id, out var row) ? row.Version : 0;
            if (stored != aggregate.Version || (row is null && aggregate.Version != 0))
            {
                throw new ConcurrencyException();
            }

            aggregate.SetPersistedVersion(aggregate.Version + 1);
            rows[id] = copy(aggregate);
        }
    }

    public void Delete(TId id)
    {
        lock (rows)
        {
            rows.Remove(id);
        }
    }
}
