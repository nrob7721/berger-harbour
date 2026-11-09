using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Period type + date → stay (§4 decision 7, §7.7 rule 2, §12.1).</summary>
public sealed class StayPeriodFactory
{
    public const string InvalidShape = "Those dates don't match the selected period.";

    /// <summary>
    /// The period of that type containing the clicked day: Mid-week Mon–Fri (clicking Fri gives the Mon→Fri ending that
    /// day), Weekend Fri→Mon (clicking Mon gives the weekend ending that day), Week starting on the clicked Mon or Fri.
    /// Null when the day isn't clickable in that mode.
    /// </summary>
    public StayRange? ForClickedDay(PeriodType periodType, DateOnly day) => periodType switch
    {
        PeriodType.Midweek when day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday =>
            Range(day.AddDays(-((int)day.DayOfWeek - 1)), 4),
        PeriodType.Weekend when day.DayOfWeek is DayOfWeek.Friday => Range(day, 3),
        PeriodType.Weekend when day.DayOfWeek is DayOfWeek.Saturday => Range(day.AddDays(-1), 3),
        PeriodType.Weekend when day.DayOfWeek is DayOfWeek.Sunday => Range(day.AddDays(-2), 3),
        PeriodType.Weekend when day.DayOfWeek is DayOfWeek.Monday => Range(day.AddDays(-3), 3),
        PeriodType.Week when day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday => Range(day, 7),
        _ => null,
    };

    /// <summary>The admin pre-fill for a day: Mon–Thu → Mid-week of that week, Fri–Sun → the weekend containing it.</summary>
    public (PeriodType PeriodType, StayRange Stay) DefaultForDay(DateOnly day) =>
        day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday
            ? (PeriodType.Midweek, ForClickedDay(PeriodType.Midweek, day)!.Value)
            : (PeriodType.Weekend, ForClickedDay(PeriodType.Weekend, day)!.Value);

    /// <summary>
    /// An online stay from its start: Midweek Mon +4, Weekend Fri +3, Week Mon or Fri +7. Throws a field error on
    /// <c>startDate</c> when the start doesn't fit, or on <c>periodType</c> for staff-only types.
    /// </summary>
    public StayRange ForOnlineStart(PeriodType periodType, DateOnly startDate)
    {
        if (!periodType.IsOnline())
        {
            throw new DomainValidationException("periodType", "Choose Mid-week, Weekend or Week.");
        }

        var stay = TryOnlineStart(periodType, startDate);
        return stay ?? throw new DomainValidationException("startDate", InvalidShape);
    }

    public StayRange? TryOnlineStart(PeriodType periodType, DateOnly startDate) => periodType switch
    {
        PeriodType.Midweek when startDate.DayOfWeek == DayOfWeek.Monday => Range(startDate, 4),
        PeriodType.Weekend when startDate.DayOfWeek == DayOfWeek.Friday => Range(startDate, 3),
        PeriodType.Week when startDate.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday => Range(startDate, 7),
        _ => null,
    };

    private static StayRange Range(DateOnly start, int nights) => StayRange.Create(start, start.AddDays(nights));
}
