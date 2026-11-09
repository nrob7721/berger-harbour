using System.Globalization;
using System.Text.RegularExpressions;

namespace BergerHarbour.Domain.Shared;

/// <summary>A recurring day of the year, written <c>dd-MM</c> (e.g. <c>01-12</c>). 29-02 is allowed.</summary>
public sealed partial record DayMonth : IComparable<DayMonth>
{
    private DayMonth(int day, int month)
    {
        Day = day;
        Month = month;
    }

    public int Day { get; }

    public int Month { get; }

    public static DayMonth Create(int day, int month, string field = "ranges")
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month))
        {
            throw new DomainValidationException(field, $"{day:00}-{month:00} is not a valid day and month.");
        }

        return new DayMonth(day, month);
    }

    public static DayMonth Parse(string? text, string field = "ranges")
    {
        var trimmed = (text ?? string.Empty).Trim();
        var match = Pattern().Match(trimmed);
        if (!match.Success)
        {
            throw new DomainValidationException(field, "Use the format dd-MM, e.g. 01-12.");
        }

        var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month))
        {
            throw new DomainValidationException(field, $"{trimmed} is not a valid day and month.");
        }

        return new DayMonth(day, month);
    }

    public static DayMonth Of(DateOnly date) => new(date.Day, date.Month);

    public int CompareTo(DayMonth? other) =>
        other is null ? 1 : Month != other.Month ? Month.CompareTo(other.Month) : Day.CompareTo(other.Day);

    public override string ToString() => $"{Day:00}-{Month:00}";

    [GeneratedRegex("^([0-9]{2})-([0-9]{2})$")]
    private static partial Regex Pattern();
}
