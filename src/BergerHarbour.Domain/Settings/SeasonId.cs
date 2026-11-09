namespace BergerHarbour.Domain.Settings;

/// <summary>Typed id of a season.</summary>
public readonly record struct SeasonId(string Value)
{
    public static SeasonId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
