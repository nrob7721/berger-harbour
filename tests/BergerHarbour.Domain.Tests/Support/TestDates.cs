using System.Globalization;

namespace BergerHarbour.Domain.Tests.Support;

public static class TestDates
{
    public static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A UTC instant.</summary>
    public static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
