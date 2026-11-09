namespace BergerHarbour.Domain.Boats;

/// <summary>Typed id of a boat.</summary>
public readonly record struct BoatId(string Value)
{
    public static BoatId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
