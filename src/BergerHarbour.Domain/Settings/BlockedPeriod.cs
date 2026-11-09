using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>Dates customers can't book online (Christmas/New Year, Easter, long weekends). Inclusive nights.</summary>
public sealed class BlockedPeriod : AggregateRoot
{
    private BlockedPeriod(BlockedPeriodId id, string name, NightRange nights, bool usesExtendedPaymentSchedule)
    {
        Id = id;
        Name = name;
        Nights = nights;
        UsesExtendedPaymentSchedule = usesExtendedPaymentSchedule;
    }

    public BlockedPeriodId Id { get; }

    public string Name { get; private set; }

    public NightRange Nights { get; private set; }

    public bool UsesExtendedPaymentSchedule { get; private set; }

    public static BlockedPeriod Create(BlockedPeriodId id, string? name, NightRange nights, bool usesExtendedPaymentSchedule) =>
        new(id, ValidName(name), nights, usesExtendedPaymentSchedule);

    public static BlockedPeriod Rehydrate(BlockedPeriodId id, string name, NightRange nights, bool extended, int version)
    {
        var period = new BlockedPeriod(id, name, nights, extended);
        period.SetPersistedVersion(version);
        return period;
    }

    public void Update(string? name, NightRange nights, bool usesExtendedPaymentSchedule)
    {
        Name = ValidName(name);
        Nights = nights;
        UsesExtendedPaymentSchedule = usesExtendedPaymentSchedule;
    }

    public bool Overlaps(StayRange stay) => stay.Overlaps(Nights);

    private static string ValidName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        return trimmed.Length <= 100 ? trimmed : throw new DomainValidationException("name", "Name must be 100 characters or fewer.");
    }
}
