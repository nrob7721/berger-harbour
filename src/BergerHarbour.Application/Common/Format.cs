using System.Globalization;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Common;

/// <summary>Australian display formats used in emails and messages. Never American month-first dates.</summary>
public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>$1,234.00</summary>
    public static string Money(decimal amount) => amount < 0
        ? "-$" + (-amount).ToString("N2", Invariant)
        : "$" + amount.ToString("N2", Invariant);

    /// <summary>dd/MM/yyyy</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Invariant);

    /// <summary>11am, 11:30am</summary>
    public static string Time(TimeOnly time) =>
        time.ToString(time.Minute == 0 ? "htt" : "h:mmtt", Invariant).ToLowerInvariant();

    /// <summary>dd/MM/yyyy h:mma in Australia/Sydney.</summary>
    public static string Timestamp(DateTimeOffset instant)
    {
        var local = SydneyTime.ToLocal(instant);
        return $"{local.ToString("dd/MM/yyyy", Invariant)} {Time(TimeOnly.FromDateTime(local.DateTime))}";
    }

    /// <summary>"11am – 2pm AEDT/NSW": the standard check-in window with the time zone for that date.</summary>
    public static string CheckInWindow(DateOnly date) =>
        $"{Time(Stay.CheckInTime)} – {Time(Stay.CheckInWindowEnd)} {SydneyTime.ZoneLabel(date, Stay.CheckInTime)}";

    /// <summary>A time on a date with its time zone, e.g. "8am AEST/NSW".</summary>
    public static string TimeWithZone(DateOnly date, TimeOnly time) => $"{Time(time)} {SydneyTime.ZoneLabel(date, time)}";

    /// <summary>National format for Australian numbers, international for others.</summary>
    public static string Phone(string? e164) => PhoneNumber.Display(e164);
}
