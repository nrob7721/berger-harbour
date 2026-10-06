namespace BergerHarbour.Domain.Shared;

/// <summary>All business dates are Australia/Sydney local dates.</summary>
public static class SydneyTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(ToLocal(utcNow).DateTime);

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>The UTC instant of a Sydney local date and time.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateTimeOffset CheckInInstant(DateOnly startDate) => ToInstant(startDate, Stay.CheckInTime);
}
