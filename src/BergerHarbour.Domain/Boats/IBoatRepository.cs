using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

public interface IBoatRepository
{
    Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default);

    Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default);

    /// <summary>Inserts a new boat (Version 0) or updates one with an optimistic version check.</summary>
    Task SaveAsync(Boat boat, CancellationToken ct = default);
}
