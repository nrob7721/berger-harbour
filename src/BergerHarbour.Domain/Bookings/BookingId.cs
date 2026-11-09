namespace BergerHarbour.Domain.Bookings;

/// <summary>Typed id of a booking.</summary>
public readonly record struct BookingId(string Value)
{
    public static BookingId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
