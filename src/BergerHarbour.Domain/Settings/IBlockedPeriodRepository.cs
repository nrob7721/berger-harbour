using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

public interface IBlockedPeriodRepository
{
    Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default);

    Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default);

    Task SaveAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default);

    Task DeleteAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default);
}
