using System.Globalization;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>A recurring day of the year, written "dd-MM" (e.g. "01-12" is 1 December).</summary>
public readonly record struct DayMonth : IComparable<DayMonth>
{
    public DayMonth(int day, int month)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month))
        {
            throw new DomainValidationException("ranges", $"{day:00}-{month:00} is not a valid day and month.");
        }

        Day = day;
        Month = month;
    }

    public int Day { get; }

    public int Month { get; }

    public static DayMonth Of(DateOnly date) => new(date.Day, date.Month);

    public static DayMonth Parse(string value)
    {
        if (DateOnly.TryParseExact($"{value}-2024", "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date))
        {
            return Of(date);
        }

        throw new DomainValidationException("ranges", $"'{value}' is not a valid day-month (expected dd-MM).");
    }

    public int CompareTo(DayMonth other) => (Month, Day).CompareTo((other.Month, other.Day));

    public static bool operator <(DayMonth a, DayMonth b) => a.CompareTo(b) < 0;
    public static bool operator >(DayMonth a, DayMonth b) => a.CompareTo(b) > 0;
    public static bool operator <=(DayMonth a, DayMonth b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DayMonth a, DayMonth b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Day:00}-{Month:00}";
}

/// <summary>A recurring inclusive range of days, which may wrap the year end (e.g. 01-12 → 31-01).</summary>
public sealed record DayMonthRange(DayMonth Start, DayMonth End)
{
    public bool WrapsYearEnd => End < Start;

    public bool Contains(DayMonth day) => WrapsYearEnd ? day >= Start || day <= End : day >= Start && day <= End;

    public bool Contains(DateOnly date) => Contains(DayMonth.Of(date));

    /// <summary>Every day of a leap year that the range covers; used to detect overlaps between recurring ranges.</summary>
    public IEnumerable<DayMonth> Days()
    {
        for (var d = new DateOnly(2024, 1, 1); d.Year == 2024; d = d.AddDays(1))
        {
            var dm = DayMonth.Of(d);
            if (Contains(dm))
            {
                yield return dm;
            }
        }
    }

    public bool Overlaps(DayMonthRange other) => Days().Any(other.Contains);

    public override string ToString() => $"{Start} → {End}";
}
