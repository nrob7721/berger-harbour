namespace BergerHarbour.Domain.Settings;

/// <summary>Typed id of an add-on definition.</summary>
public readonly record struct AddonId(string Value)
{
    public static AddonId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
