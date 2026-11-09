namespace BergerHarbour.Domain.Settings;

public interface IBlockedPeriodRepository
{
    Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default);

    Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default);

    Task SaveAsync(BlockedPeriod period, CancellationToken ct = default);

    Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default);
}
