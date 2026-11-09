namespace BergerHarbour.Domain.Boats;

public interface IBoatRepository
{
    /// <summary>Every boat, active or not.</summary>
    Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default);

    Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default);

    Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task SaveAsync(Boat boat, CancellationToken ct = default);
}
