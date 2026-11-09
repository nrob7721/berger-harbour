namespace BergerHarbour.Domain.Bookings;

/// <summary>Derived from the amounts paid (§7.3).</summary>
public enum PaymentStatus
{
    Outstanding,
    DepositPaid,
    PartPaid,
    FullyPaid,
}
