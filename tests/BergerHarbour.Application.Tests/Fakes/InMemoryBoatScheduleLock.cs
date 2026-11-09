using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>Runs one exclusive section at a time and applies its writes only when the work succeeds.</summary>
public sealed class InMemoryBoatScheduleLock(InMemoryDatabase db) : IBoatScheduleLock
{
    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work)
    {
        await db.ScheduleGate.WaitAsync();
        try
        {
            var session = new InMemoryBoatScheduleSession(db, boatId);
            var result = await work(session);
            session.Commit();
            return result;
        }
        finally
        {
            db.ScheduleGate.Release();
        }
    }
}
