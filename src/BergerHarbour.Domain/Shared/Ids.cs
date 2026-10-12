using System.Security.Cryptography;

namespace BergerHarbour.Domain.Shared;

public static class IdGenerator
{
    public static string NewId() => Guid.NewGuid().ToString("N");
}

public readonly record struct BoatId(string Value)
{
    public static BoatId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct BookingId(string Value)
{
    public static BookingId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct CustomerId(string Value)
{
    public static CustomerId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct UnavailabilityId(string Value)
{
    public static UnavailabilityId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct SeasonId(string Value)
{
    public static readonly SeasonId Normal = new("normal");
    public static SeasonId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct BlockedPeriodId(string Value)
{
    public static BlockedPeriodId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

public readonly record struct AddonId(string Value)
{
    public static AddonId New() => new(IdGenerator.NewId());
    public override string ToString() => Value;
}

/// <summary>Random and unguessable (192 bits, base64url): the booking app holds it as its only credential.</summary>
public readonly record struct BookingDraftId(string Value)
{
    public static BookingDraftId New() =>
        new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

    public override string ToString() => Value;
}
