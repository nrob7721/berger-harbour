using System.Globalization;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>Check-in/check-out times on the API: <c>HH:mm</c>.</summary>
public static class StaffTimes
{
    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Parses <c>HH:mm</c>; blank gives the default.</summary>
    public static TimeOnly Parse(string? text, TimeOnly defaultTime, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultTime;
        }

        return TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new DomainValidationException(field, "Enter a time.");
    }
}
