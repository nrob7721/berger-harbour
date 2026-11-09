using BergerHarbour.Domain.Boats;

namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>
/// Reads of unavailabilities. Creating, editing and deleting one changes occupancy, so those writes go through the
/// boat schedule lock's session.
/// </summary>
public interface IBoatUnavailabilityRepository
{
    Task<BoatUnavailability?> GetAsync(BoatUnavailabilityId id, CancellationToken ct = default);

    Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default);

    /// <summary>Every boat's unavailabilities with a night in <c>[from, to)</c>.</summary>
    Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
