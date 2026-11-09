namespace BergerHarbour.Domain.Shared;

/// <summary>Australia/Sydney local dates and times. Every date in the system is a Sydney date.</summary>
public static class Sydney
{
    public static readonly TimeOnly StandardCheckIn = new(11, 0);
    public static readonly TimeOnly StandardCheckInEnd = new(14, 0);
    public static readonly TimeOnly StandardCheckOut = new(8, 0);

    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>The instant of a Sydney wall-clock time.</summary>
    public static DateTimeOffset InstantOf(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // inside the daylight-saving gap: the clock jumps forward
        }

        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>"AEDT/NSW" or "AEST/NSW", whichever applies on that date (at midday).</summary>
    public static string ZoneLabel(DateOnly date) =>
        Zone.IsDaylightSavingTime(date.ToDateTime(new TimeOnly(12, 0))) ? "AEDT/NSW" : "AEST/NSW";
}
