namespace BergerHarbour.Domain.Shared;

/// <summary>Groupings of <see cref="PeriodType"/> used by several rules.</summary>
public static class PeriodTypes
{
    /// <summary>The period types customers can book online, which also carry add-on prices.</summary>
    public static readonly IReadOnlyList<PeriodType> Online = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week];

    /// <summary>The period types a boat has rates for.</summary>
    public static readonly IReadOnlyList<PeriodType> Rated = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week, PeriodType.LongWeekend];

    public static bool IsOnline(this PeriodType type) => type is PeriodType.Midweek or PeriodType.Weekend or PeriodType.Week;
}
