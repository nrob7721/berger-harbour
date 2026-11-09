using System.Globalization;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Notifications;

/// <summary>The system's display format (§3): dd/MM/yyyy dates, 11am / 11:30am times, $1,234.00 money.</summary>
public static class DisplayFormat
{
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DateWithDay(DateOnly date) => date.ToString("ddd dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Money(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time)
    {
        var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        var suffix = time.Hour < 12 ? "am" : "pm";
        return time.Minute == 0
            ? $"{hour}{suffix}"
            : $"{hour}:{time.Minute.ToString("00", CultureInfo.InvariantCulture)}{suffix}";
    }

    public static string Timestamp(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Sydney.Zone);
        return $"{Date(DateOnly.FromDateTime(local.DateTime))} {Time(TimeOnly.FromDateTime(local.DateTime))}";
    }

    /// <summary>"11am – 2pm AEDT/NSW" for the standard window, or the custom time with the date's zone label.</summary>
    public static string CheckIn(DateOnly date, TimeOnly time) =>
        (time == Sydney.StandardCheckIn ? $"{Time(Sydney.StandardCheckIn)} – {Time(Sydney.StandardCheckInEnd)}" : Time(time)) + " " + Sydney.ZoneLabel(date);

    /// <summary>"8am AEDT/NSW", or the custom time.</summary>
    public static string CheckOut(DateOnly date, TimeOnly time) => $"{Time(time)} {Sydney.ZoneLabel(date)}";
}
