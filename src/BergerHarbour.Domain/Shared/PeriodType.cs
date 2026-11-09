namespace BergerHarbour.Domain.Shared;

/// <summary>Midweek (Mon→Fri), Weekend (Fri→Mon), Week (Mon→Mon or Fri→Fri); LongWeekend and Custom are staff only.</summary>
public enum PeriodType
{
    Midweek,
    Weekend,
    Week,
    LongWeekend,
    Custom,
}
