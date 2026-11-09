using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBoatUnavailabilityRepository(InMemoryDatabase db) : IBoatUnavailabilityRepository
{
    public Task<BoatUnavailability?> GetAsync(BoatUnavailabilityId id, CancellationToken ct = default) => Task.FromResult(db.Unavailabilities.Get(id));

    public Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BoatUnavailability>>(db.Unavailabilities.All.Where(u => u.BoatId == boatId).ToList());

    public Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BoatUnavailability>>(
            db.Unavailabilities.All.Where(u => u.Nights.LastNight >= from && u.Nights.FirstNight < to).ToList());
}
