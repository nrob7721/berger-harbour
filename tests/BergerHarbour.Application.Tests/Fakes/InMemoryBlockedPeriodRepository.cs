using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBlockedPeriodRepository(InMemoryDatabase db) : IBlockedPeriodRepository
{
    public Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default) => Task.FromResult(db.BlockedPeriods.All);

    public Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default) => Task.FromResult(db.BlockedPeriods.Get(id));

    public Task SaveAsync(BlockedPeriod period, CancellationToken ct = default)
    {
        db.BlockedPeriods.Save(period);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default)
    {
        db.BlockedPeriods.Delete(period.Id);
        return Task.CompletedTask;
    }
}
