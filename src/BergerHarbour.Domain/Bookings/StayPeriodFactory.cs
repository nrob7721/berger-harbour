using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Maps a period type and a date to a stay. In the customer calendar a click selects the period of the selected
/// type that contains the clicked day; in Week mode only Mondays and Fridays are clickable and start the stay.
/// </summary>
public static class StayPeriodFactory
{
    public const int MidweekNights = 4;
    public const int WeekendNights = 3;
    public const int WeekNights = 7;
    public const int LongWeekendNights = 4;

    /// <summary>The stay a calendar click maps to, or null when the day is not clickable in that mode.</summary>
    public static Stay? FromClickedDay(PeriodType periodType, DateOnly clicked)
    {
        var day = clicked.DayOfWeek;
        return periodType switch
        {
            PeriodType.Midweek when day is >= DayOfWeek.Monday and <= DayOfWeek.Friday =>
                StartingOn(clicked.AddDays(-(day - DayOfWeek.Monday)), MidweekNights),
            PeriodType.Weekend when day is DayOfWeek.Friday => StartingOn(clicked, WeekendNights),
            PeriodType.Weekend when day is DayOfWeek.Saturday => StartingOn(clicked.AddDays(-1), WeekendNights),
            PeriodType.Weekend when day is DayOfWeek.Sunday => StartingOn(clicked.AddDays(-2), WeekendNights),
            PeriodType.Weekend when day is DayOfWeek.Monday => StartingOn(clicked.AddDays(-3), WeekendNights),
            PeriodType.Week when day is DayOfWeek.Monday or DayOfWeek.Friday => StartingOn(clicked, WeekNights),
            _ => null,
        };
    }

    /// <summary>True when the stay has exactly the shape of an online period of that type.</summary>
    public static bool IsValidOnlineShape(PeriodType periodType, Stay stay)
    {
        var start = stay.StartDate.DayOfWeek;
        return periodType switch
        {
            PeriodType.Midweek => start == DayOfWeek.Monday && stay.Nights == MidweekNights,
            PeriodType.Weekend => start == DayOfWeek.Friday && stay.Nights == WeekendNights,
            PeriodType.Week => start is DayOfWeek.Monday or DayOfWeek.Friday && stay.Nights == WeekNights,
            _ => false,
        };
    }

    /// <summary>The online stay that starts on the given date, or null when no period of that type starts then.</summary>
    public static Stay? OnlineStayStartingOn(PeriodType periodType, DateOnly startDate)
    {
        var nights = periodType switch
        {
            PeriodType.Midweek => MidweekNights,
            PeriodType.Weekend => WeekendNights,
            PeriodType.Week => WeekNights,
            _ => 0,
        };
        if (nights == 0)
        {
            return null;
        }

        var stay = StartingOn(startDate, nights);
        return IsValidOnlineShape(periodType, stay) ? stay : null;
    }

    /// <summary>The end date the staff booking dialog pre-fills for a period type, or null for Custom.</summary>
    public static DateOnly? DefaultEndDate(PeriodType periodType, DateOnly startDate) => periodType switch
    {
        PeriodType.Midweek => startDate.AddDays(MidweekNights),
        PeriodType.Weekend => startDate.AddDays(WeekendNights),
        PeriodType.Week => startDate.AddDays(WeekNights),
        PeriodType.LongWeekend => startDate.AddDays(LongWeekendNights),
        _ => null,
    };

    private static Stay StartingOn(DateOnly start, int nights) => new(start, start.AddDays(nights));
}
