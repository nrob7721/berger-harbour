using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBoatRepository(InMemoryDatabase db) : IBoatRepository
{
    public Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default) => Task.FromResult(db.Boats.All);

    public Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default) => Task.FromResult(db.Boats.Get(id));

    public Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default) => Task.FromResult(db.Boats.All.FirstOrDefault(b => b.Slug == slug));

    public Task SaveAsync(Boat boat, CancellationToken ct = default)
    {
        db.Boats.Save(boat);
        return Task.CompletedTask;
    }
}
