namespace BergerHarbour.Domain.Shared;

public enum PeriodType
{
    Midweek,
    Weekend,
    Week,
    LongWeekend,
    Custom,
}

public static class PeriodTypes
{
    /// <summary>The period types a customer can book online.</summary>
    public static readonly IReadOnlyList<PeriodType> Online = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week];

    /// <summary>The period types a boat has rates for.</summary>
    public static readonly IReadOnlyList<PeriodType> Rated = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week, PeriodType.LongWeekend];

    /// <summary>The period types a boat must have rates for before it can be activated.</summary>
    public static readonly IReadOnlyList<PeriodType> RequiredForActivation = Online;
}
