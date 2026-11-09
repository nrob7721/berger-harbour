namespace BergerHarbour.Domain.Customers;

/// <summary>Typed id of a customer.</summary>
public readonly record struct CustomerId(string Value)
{
    public static CustomerId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
