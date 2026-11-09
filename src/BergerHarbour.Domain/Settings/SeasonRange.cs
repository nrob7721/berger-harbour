using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>A recurring day-month range of a season, inclusive at both ends. It may wrap the year end (01-12 → 31-01).</summary>
public sealed record SeasonRange(DayMonth Start, DayMonth End)
{
    public bool Wraps => Start.CompareTo(End) > 0;

    public bool Contains(DateOnly date)
    {
        var day = DayMonth.Of(date);
        return Wraps
            ? day.CompareTo(Start) >= 0 || day.CompareTo(End) <= 0
            : day.CompareTo(Start) >= 0 && day.CompareTo(End) <= 0;
    }

    /// <summary>Exact check over every day of a leap year.</summary>
    public bool Overlaps(SeasonRange other)
    {
        for (var date = new DateOnly(2024, 1, 1); date.Year == 2024; date = date.AddDays(1))
        {
            if (Contains(date) && other.Contains(date))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => $"{Start} → {End}";
}
