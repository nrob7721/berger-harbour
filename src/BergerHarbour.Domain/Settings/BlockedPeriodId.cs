namespace BergerHarbour.Domain.Settings;

/// <summary>Typed id of a blocked period.</summary>
public readonly record struct BlockedPeriodId(string Value)
{
    public static BlockedPeriodId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
