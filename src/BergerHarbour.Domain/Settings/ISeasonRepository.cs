using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

public interface ISeasonRepository
{
    Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default);

    Task<Season?> GetAsync(SeasonId id, CancellationToken ct = default);

    Task SaveAsync(Season season, CancellationToken ct = default);

    Task DeleteAsync(Season season, CancellationToken ct = default);
}
