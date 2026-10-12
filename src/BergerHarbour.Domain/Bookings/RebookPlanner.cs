using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>The dates and period type a "Re-book" pre-fills.</summary>
public sealed record RebookDates(PeriodType PeriodType, Stay Stay);

/// <summary>
/// Re-booking keeps the day and month and the number of nights, moved to the first later year in which the stay
/// starts today or later (e.g. 23/10/2021 → 28/10/2021, re-booked on 10/03/2023, becomes 23/10/2023 → 28/10/2023).
/// 29/02 becomes 28/02 in a non-leap year.
/// </summary>
public static class RebookPlanner
{
    public static RebookDates Plan(PeriodType periodType, Stay original, DateOnly today)
    {
        var year = original.StartDate.Year + 1;
        var start = SameDayIn(original.StartDate, year);
        while (start < today)
        {
            start = SameDayIn(original.StartDate, ++year);
        }

        var stay = new Stay(start, start.AddDays(original.Nights));
        return new RebookDates(PeriodTypeFor(periodType, stay), stay);
    }

    /// <summary>
    /// Midweek, Weekend and Week are kept only when the new dates still have that shape; LongWeekend is always kept;
    /// anything else becomes Custom.
    /// </summary>
    private static PeriodType PeriodTypeFor(PeriodType original, Stay stay) => original switch
    {
        PeriodType.LongWeekend => PeriodType.LongWeekend,
        PeriodType.Midweek or PeriodType.Weekend or PeriodType.Week when StayPeriodFactory.IsValidOnlineShape(original, stay) => original,
        _ => PeriodType.Custom,
    };

    private static DateOnly SameDayIn(DateOnly date, int year) =>
        new(year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month)));
}
