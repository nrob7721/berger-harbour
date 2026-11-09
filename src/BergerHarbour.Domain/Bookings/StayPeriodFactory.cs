using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Maps period types and dates to stays.</summary>
public static class StayPeriodFactory
{
    public static readonly PeriodType[] OnlinePeriodTypes = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week];

    /// <summary>The period of the type that contains the clicked day, or null when the day isn't clickable in that mode.</summary>
    public static StayRange? ForClick(PeriodType periodType, DateOnly day) => periodType switch
    {
        PeriodType.Midweek => day.DayOfWeek switch
        {
            DayOfWeek.Saturday or DayOfWeek.Sunday => null,
            // Mon–Fri of that week; clicking Fri selects the Mon→Fri ending that day.
            _ => Midweek(day.AddDays(-((int)day.DayOfWeek - 1))),
        },
        PeriodType.Weekend => day.DayOfWeek switch
        {
            DayOfWeek.Friday => Weekend(day),
            DayOfWeek.Saturday => Weekend(day.AddDays(-1)),
            DayOfWeek.Sunday => Weekend(day.AddDays(-2)),
            DayOfWeek.Monday => Weekend(day.AddDays(-3)), // the weekend ending that day
            _ => null,
        },
        PeriodType.Week => day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday ? new StayRange(day, day.AddDays(7)) : null,
        _ => null,
    };

    private static StayRange Midweek(DateOnly monday) => new(monday, monday.AddDays(4));
    private static StayRange Weekend(DateOnly friday) => new(friday, friday.AddDays(3));

    /// <summary>The online stay that starts on <paramref name="start"/>, or a field error when the start day doesn't fit the period type.</summary>
    public static StayRange ForOnlineStart(PeriodType periodType, DateOnly start) => periodType switch
    {
        PeriodType.Midweek when start.DayOfWeek == DayOfWeek.Monday => Midweek(start),
        PeriodType.Midweek => throw new DomainValidationException("startDate", "A mid-week stay starts on a Monday."),
        PeriodType.Weekend when start.DayOfWeek == DayOfWeek.Friday => Weekend(start),
        PeriodType.Weekend => throw new DomainValidationException("startDate", "A weekend stay starts on a Friday."),
        PeriodType.Week when start.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday => new StayRange(start, start.AddDays(7)),
        PeriodType.Week => throw new DomainValidationException("startDate", "A week stay starts on a Monday or a Friday."),
        _ => throw new DomainValidationException("periodType", "Online bookings are for a mid-week, weekend or week stay. Please call or email us for other stays."),
    };

    public static bool IsValidOnlineShape(PeriodType periodType, StayRange stay)
    {
        try { return ForOnlineStart(periodType, stay.Start) == stay; }
        catch (DomainValidationException) { return false; }
    }

    /// <summary>The end date staff get pre-filled when they pick a period type; null for Custom.</summary>
    public static DateOnly? DefaultEnd(PeriodType periodType, DateOnly start) => periodType switch
    {
        PeriodType.Midweek => start.AddDays(4),
        PeriodType.Weekend => start.AddDays(3),
        PeriodType.Week => start.AddDays(7),
        PeriodType.LongWeekend => start.AddDays(4),
        _ => null,
    };
}
