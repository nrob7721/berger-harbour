using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>A date range customers can't book online (Christmas/New Year, Easter, long weekends …).</summary>
public sealed class BlockedPeriod : AggregateRoot
{
    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public NightRange Nights { get; private set; }
    public bool UsesExtendedPaymentSchedule { get; private set; }

    private BlockedPeriod() { }

    public static BlockedPeriod Create(string name, DateOnly firstNight, DateOnly lastNight, bool usesExtendedSchedule, DateTimeOffset now)
    {
        var p = new BlockedPeriod { Id = Ids.New() };
        p.Update(name, firstNight, lastNight, usesExtendedSchedule);
        p.InitialiseAudit(now);
        return p;
    }

    public static BlockedPeriod Rehydrate(string id, string name, NightRange nights, bool extended, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var p = new BlockedPeriod { Id = id, Name = name, Nights = nights, UsesExtendedPaymentSchedule = extended };
        p.RestoreAudit(version, created, modified);
        return p;
    }

    public void Update(string name, DateOnly firstNight, DateOnly lastNight, bool usesExtendedSchedule)
    {
        var errors = new ValidationErrors();
        var n = errors.Capture(() => Text.Required(name, "name", "Name", 100));
        var nights = errors.Capture(() => (NightRange?)new NightRange(firstNight, lastNight));
        errors.ThrowIfAny();
        Name = n!;
        Nights = nights!.Value;
        UsesExtendedPaymentSchedule = usesExtendedSchedule;
    }
}

public interface IBlockedPeriodRepository
{
    Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default);
    Task<BlockedPeriod?> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(BlockedPeriod period, CancellationToken ct = default);
    Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default);
}
