namespace BergerHarbour.Domain.Bookings;

/// <summary>A payment milestone of a booking's schedule.</summary>
public enum MilestoneKind
{
    Deposit,
    Balance,
    First,
    Final,
}
