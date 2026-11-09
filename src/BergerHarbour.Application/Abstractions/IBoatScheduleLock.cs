using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Application.Abstractions;

/// <summary>
/// A per-boat exclusive section for every write that can create or move occupancy (§7.2). Concurrent sections on the
/// same boat are serialised; the work may run more than once and must not have side effects outside the session.
/// </summary>
public interface IBoatScheduleLock
{
    Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work);
}
