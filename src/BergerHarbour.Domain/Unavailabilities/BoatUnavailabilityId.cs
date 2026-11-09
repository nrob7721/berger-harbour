namespace BergerHarbour.Domain.Unavailabilities;

/// <summary>Typed id of a boat unavailability.</summary>
public readonly record struct BoatUnavailabilityId(string Value)
{
    public static BoatUnavailabilityId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
