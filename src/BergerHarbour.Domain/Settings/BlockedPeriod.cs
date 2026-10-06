using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>
/// A staff-defined range (Christmas/New Year, Easter, long weekends, …) that customers cannot book online.
/// </summary>
public sealed class BlockedPeriod : AggregateRoot<BlockedPeriodId>
{
    private BlockedPeriod(BlockedPeriodId id, int version, string name, NightRange nights, bool usesExtendedPaymentSchedule)
        : base(id, version)
    {
        Name = name;
        Nights = nights;
        UsesExtendedPaymentSchedule = usesExtendedPaymentSchedule;
    }

    public string Name { get; private set; }

    public NightRange Nights { get; private set; }

    public bool UsesExtendedPaymentSchedule { get; private set; }

    public static BlockedPeriod Create(string name, DateOnly firstNight, DateOnly lastNight, bool usesExtendedPaymentSchedule) =>
        CreateWithId(BlockedPeriodId.New(), name, firstNight, lastNight, usesExtendedPaymentSchedule);

    /// <summary>Seeds use stable ids so the seed is idempotent.</summary>
    public static BlockedPeriod CreateWithId(BlockedPeriodId id, string name, DateOnly firstNight, DateOnly lastNight,
        bool usesExtendedPaymentSchedule) =>
        new(id, 0, Guard.Required(name, "name", "Name", 100), new NightRange(firstNight, lastNight),
            usesExtendedPaymentSchedule);

    public void Update(string name, DateOnly firstNight, DateOnly lastNight, bool usesExtendedPaymentSchedule)
    {
        Name = Guard.Required(name, "name", "Name", 100);
        Nights = new NightRange(firstNight, lastNight);
        UsesExtendedPaymentSchedule = usesExtendedPaymentSchedule;
    }

    public BlockedPeriodSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, Nights.FirstNight, Nights.LastNight, UsesExtendedPaymentSchedule);

    public static BlockedPeriod FromSnapshot(BlockedPeriodSnapshot s) =>
        new(new BlockedPeriodId(s.Id), s.Version, s.Name, new NightRange(s.FirstNight, s.LastNight),
            s.UsesExtendedPaymentSchedule);
}

public sealed record BlockedPeriodSnapshot(
    string Id,
    int Version,
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight,
    bool UsesExtendedPaymentSchedule);
