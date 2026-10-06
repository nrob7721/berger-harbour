using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>
/// Reads and deletes unavailabilities. Creating or moving one changes occupancy, so those writes go through the
/// per-boat exclusive section instead.
/// </summary>
public interface IBoatUnavailabilityRepository
{
    Task<BoatUnavailability?> GetAsync(UnavailabilityId id, CancellationToken ct = default);

    Task<IReadOnlyList<BoatUnavailability>> ListByBoatAsync(BoatId boatId, CancellationToken ct = default);

    /// <summary>Unavailabilities of any boat with at least one night in [from, to).</summary>
    Task<IReadOnlyList<BoatUnavailability>> ListOverlappingAsync(DateOnly from, DateOnly to, BoatId? boatId = null,
        CancellationToken ct = default);

    Task DeleteAsync(BoatUnavailability unavailability, CancellationToken ct = default);
}
