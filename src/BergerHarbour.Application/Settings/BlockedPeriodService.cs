using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>Blocked periods (<c>BlockedPeriodsController</c>): Christmas/New Year, Easter and long weekends, entered yearly.</summary>
public sealed class BlockedPeriodService
{
    private readonly IBlockedPeriodRepository periods;

    public BlockedPeriodService(IBlockedPeriodRepository periods)
    {
        this.periods = periods;
    }

    public async Task<IReadOnlyList<BlockedPeriodDto>> ListAsync(CancellationToken ct = default) =>
        (await periods.ListAsync(ct)).OrderBy(p => p.Nights.FirstNight).Select(ToDto).ToList();

    public async Task<BlockedPeriodDto> CreateAsync(SaveBlockedPeriodRequest request, CancellationToken ct = default)
    {
        var period = BlockedPeriod.Create(BlockedPeriodId.New(), request.Name, NightRange.Create(request.FirstNight, request.LastNight),
            request.UsesExtendedPaymentSchedule);
        await periods.SaveAsync(period, ct);
        return ToDto(period);
    }

    public async Task<BlockedPeriodDto> UpdateAsync(string id, SaveBlockedPeriodRequest request, CancellationToken ct = default)
    {
        var period = await LoadAsync(id, ct);
        period.EnsureVersion(request.Version);
        period.Update(request.Name, NightRange.Create(request.FirstNight, request.LastNight), request.UsesExtendedPaymentSchedule);
        await periods.SaveAsync(period, ct);
        return ToDto(period);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default) => await periods.DeleteAsync(await LoadAsync(id, ct), ct);

    private static BlockedPeriodDto ToDto(BlockedPeriod p) =>
        new(p.Id.Value, p.Name, p.Nights.FirstNight, p.Nights.LastNight, p.UsesExtendedPaymentSchedule, p.Version);

    private async Task<BlockedPeriod> LoadAsync(string id, CancellationToken ct) =>
        await periods.GetAsync(new BlockedPeriodId(id), ct) ?? throw new NotFoundException("We couldn't find that blocked period.");
}
